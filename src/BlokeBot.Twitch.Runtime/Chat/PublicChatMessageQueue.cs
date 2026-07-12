using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using BlokeBot.Eventing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatMessageQueue(
    TwitchBotSettings settings,
    TimeProvider timeProvider,
    PublicChatQueueBacklogMonitor backlogMonitor,
    PublicChatQueueAlertDispatcher alertDispatcher,
    IPublicChatOutbox outbox,
    IPublicChatTransport transport,
    ILogger<PublicChatMessageQueue> log
)
{
    private static readonly TimeSpan ClaimContentionDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);
    private readonly Channel<bool> wakeSignals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        }
    );
    private int running;

    public async ValueTask<PublicChatOutboxReceipt> EnqueueAsync(
        PublicChatEnqueueCommand command,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(command);
        if (
            string.IsNullOrWhiteSpace(command.Channel)
            || string.IsNullOrWhiteSpace(command.Message)
        )
            return PublicChatOutboxReceipt.Empty;

        var parts = TwitchChatMessageSplitter
            .Split(command.Message, MaxMessageLength)
            .ToImmutableArray();
        if (parts.IsDefaultOrEmpty)
            return PublicChatOutboxReceipt.Empty;

        var items = parts
            .Select(part =>
                new PublicChatOutboxItem
                {
                    Message = part,
                    DeduplicationKey = PublicChatMessageDeduplication.Key(
                        command.Channel,
                        part
                    ),
                }
            )
            .ToImmutableArray();
        var receipt = await outbox.EnqueueAsync(
            new PublicChatOutboxBatch
            {
                Channel = command.Channel,
                Items = items,
                EnqueuedAt = UtcNow(),
            },
            cancellationToken
        );
        _ = wakeSignals.Writer.TryWrite(true);
        return receipt;
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0)
            throw new InvalidOperationException("The public chat outbox worker is already running.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = UtcNow();
                var nextBacklogAlert = await ObserveBacklogAsync(now, cancellationToken);
                var outcome = await outbox.TryClaimNextAsync(
                    now,
                    now + ClaimLease,
                    SendInterval,
                    DuplicateCooldown,
                    cancellationToken
                );
                switch (outcome)
                {
                    case PublicChatClaimOutcome.Claimed claimed:
                        await ProcessClaimAsync(claimed.Message, cancellationToken);
                        break;
                    case PublicChatClaimOutcome.AwaitingAvailability waiting:
                        var availabilityDelay = waiting.AvailableAt - UtcNow();
                        await WaitForSignalOrDelayAsync(
                            nextBacklogAlert is { } alertDelay
                                ? Min(availabilityDelay, alertDelay)
                                : availabilityDelay,
                            cancellationToken
                        );
                        break;
                    case PublicChatClaimOutcome.Empty:
                        _ = await wakeSignals.Reader.ReadAsync(cancellationToken);
                        break;
                    case PublicChatClaimOutcome.Contended:
                        await Task.Delay(
                            ClaimContentionDelay,
                            timeProvider,
                            cancellationToken
                        );
                        break;
                    default:
                        throw new UnreachableException(
                            $"Unknown public chat claim outcome {outcome.GetType().Name}."
                        );
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }

    private async Task ProcessClaimAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        var sendStartedAt = UtcNow();
        PublicChatClaimUpdate beginSend;
        try
        {
            beginSend = await ApplyClaimUpdateAsync(
                () =>
                    outbox.BeginSendAsync(
                        message,
                        sendStartedAt,
                        sendStartedAt + ClaimLease,
                        cancellationToken
                    ),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = await ApplyClaimUpdateAsync(
                () => outbox.ReleaseClaimAsync(message, CancellationToken.None),
                CancellationToken.None
            );
            throw;
        }
        switch (beginSend)
        {
            case PublicChatClaimUpdate.Applied:
                break;
            case PublicChatClaimUpdate.OwnershipLost:
                return;
            case PublicChatClaimUpdate.Contended:
                throw new UnreachableException(
                    "Claim contention escaped the public chat transition retry boundary."
                );
            default:
                throw new UnreachableException(
                    $"Unknown public chat claim update {beginSend.GetType().Name}."
                );
        }

        try
        {
            await transport.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = await ApplyClaimUpdateAsync(
                () => outbox.MarkFaultedAsync(message, UtcNow(), CancellationToken.None),
                CancellationToken.None
            );
            throw;
        }
        catch (Exception exception)
        {
            _ = await ApplyClaimUpdateAsync(
                () => outbox.MarkFaultedAsync(message, UtcNow(), cancellationToken),
                cancellationToken
            );
            log.LogWarning(
                "Public chat transport failed for outbox message {OutboxMessageId} in #{Channel} with {FailureType}.",
                message.Id,
                message.Channel,
                exception.GetType().FullName
            );
            return;
        }

        var completed = await ApplyClaimUpdateAsync(
            () => outbox.MarkDeliveredAsync(message, UtcNow(), cancellationToken),
            cancellationToken
        );
        if (completed is PublicChatClaimUpdate.OwnershipLost)
        {
            log.LogWarning(
                "Public chat outbox message {OutboxMessageId} lost claim ownership after delivery.",
                message.Id
            );
        }
    }

    private async Task<TimeSpan?> ObserveBacklogAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var pending = await outbox.LoadOutstandingAsync(cancellationToken);
        backlogMonitor.ResetDrainedChannels(pending);
        var alerts = backlogMonitor.CaptureAlerts(
            pending,
            now,
            QueueStuckThreshold,
            alertDispatcher.HasObservers
        );
        await NotifyQueueAlertsAsync(alerts);
        return backlogMonitor.NextAlertDelay(
            pending,
            now,
            QueueStuckThreshold,
            alertDispatcher.HasObservers
        );
    }

    private async Task NotifyQueueAlertsAsync(
        IReadOnlyList<PublicChatQueueBacklog> alerts
    )
    {
        try
        {
            await alertDispatcher.NotifyAsync(alerts, CancellationToken.None);
        }
        catch (ObserverFanOutEscalationException escalation)
        {
            ReportAlertEscalation(escalation, alerts.Count);
        }
    }

    private async Task WaitForSignalOrDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken
    )
    {
        if (delay <= TimeSpan.Zero)
            return;

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        var delayTask = Task.Delay(delay, timeProvider, waitCancellation.Token);
        var signalTask = wakeSignals.Reader.ReadAsync(waitCancellation.Token).AsTask();
        try
        {
            await await Task.WhenAny(delayTask, signalTask);
        }
        finally
        {
            await waitCancellation.CancelAsync();
        }
    }

    private async ValueTask<PublicChatClaimUpdate> ApplyClaimUpdateAsync(
        Func<ValueTask<PublicChatClaimUpdate>> update,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var result = await update();
            switch (result)
            {
                case PublicChatClaimUpdate.Applied:
                case PublicChatClaimUpdate.OwnershipLost:
                    return result;
                case PublicChatClaimUpdate.Contended:
                    await Task.Delay(
                        ClaimContentionDelay,
                        timeProvider,
                        cancellationToken
                    );
                    break;
                default:
                    throw new UnreachableException(
                        $"Unknown public chat claim update {result.GetType().Name}."
                    );
            }
        }
    }

    private TimeSpan SendInterval =>
        TimeSpan.FromSeconds(Math.Max(0, settings.ChatMessageSendIntervalSeconds));

    private TimeSpan DuplicateCooldown =>
        TimeSpan.FromSeconds(Math.Max(0, settings.DuplicateChatMessageCooldownSeconds));

    private TimeSpan QueueStuckThreshold =>
        TimeSpan.FromSeconds(Math.Max(0, settings.PublicChatQueueAlerts.StuckAfterSeconds));

    private void ReportAlertEscalation(
        ObserverFanOutEscalationException escalation,
        int alertCount
    )
    {
        var boundaries = string.Join(
            ", ",
            escalation.Failures
                .Select(failure => failure.Boundary.Value)
                .Distinct(StringComparer.Ordinal)
        );
        var events = string.Join(
            ", ",
            escalation.Failures
                .Select(failure => failure.Event.Value)
                .Distinct(StringComparer.Ordinal)
        );
        var correlations = string.Join(
            ", ",
            escalation.Failures
                .Select(failure => failure.CorrelationId.Value)
                .Distinct(StringComparer.Ordinal)
        );
        var handlingStages = string.Join(
            ", ",
            escalation.HandlingFailures
                .Select(failure => failure.Stage)
                .Distinct()
        );
        var handlingFailureTypes = string.Join(
            ", ",
            escalation.HandlingFailures
                .Select(failure => failure.FailureType)
                .Distinct(StringComparer.Ordinal)
        );
        log.LogError(
            "Public chat queue alert handling escalated for {AlertCount} alerts after {ObserverFailureCount} observer failures and {HandlingFailureCount} handling failures at {Boundaries} for {Events}; stages {HandlingStages}, failure types {HandlingFailureTypes}, correlations {CorrelationIds}. Continuing queued chat processing.",
            alertCount,
            escalation.Failures.Count,
            escalation.HandlingFailures.Count,
            boundaries,
            events,
            handlingStages,
            handlingFailureTypes,
            correlations
        );
    }

    private int MaxMessageLength => Math.Max(0, settings.MaxChatMessageLength);

    private DateTimeOffset UtcNow() => timeProvider.GetUtcNow();

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;
}

internal sealed class PublicChatOutboxWorker(PublicChatMessageQueue queue)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        queue.RunAsync(stoppingToken);
}
