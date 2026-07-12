using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
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
                        await WaitForSignalOrScheduledWakeAsync(
                            nextBacklogAlert is { } alertDelay
                                ? Min(availabilityDelay, alertDelay)
                                : availabilityDelay,
                            cancellationToken
                        );
                        break;
                    case PublicChatClaimOutcome.Empty:
                        if (nextBacklogAlert is { } emptyAlertDelay)
                        {
                            await WaitForSignalOrScheduledWakeAsync(
                                emptyAlertDelay,
                                cancellationToken
                            );
                        }
                        else
                        {
                            _ = await wakeSignals.Reader.ReadAsync(cancellationToken);
                        }
                        break;
                    case PublicChatClaimOutcome.Contended:
                        await WaitForSignalOrScheduledWakeAsync(
                            ClaimContentionDelay,
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
        PublicChatPreparationOutcome preparation;
        try
        {
            preparation = await transport.PrepareAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseClaimAfterCancellationAsync(message);
            throw;
        }
        catch (Exception exception)
        {
            preparation = PublicChatDeliveryClassifier.ClassifyPreparationFailure(
                exception,
                cancellationToken
            );
        }

        await preparation.Match(
            ready => ProcessPreparedSendAsync(ready.Send, cancellationToken),
            transient =>
                RecordOutcomeAsync(
                    message,
                    PublicChatDeliveryClassifier.MapPreparationFailure(transient),
                    CancellationToken.None
                ),
            unexpected =>
                RecordOutcomeAsync(
                    message,
                    PublicChatDeliveryClassifier.MapPreparationFailure(unexpected),
                    CancellationToken.None
                )
        );
    }

    private async Task ProcessPreparedSendAsync(
        PublicChatPreparedSend prepared,
        CancellationToken cancellationToken
    )
    {
        var message = prepared.Message;
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
            await ReleaseClaimAfterCancellationAsync(message);
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

        PublicChatDeliveryOutcome outcome;
        try
        {
            var sendResult = await transport.SendAsync(prepared, cancellationToken);
            outcome = PublicChatDeliveryClassifier.MapSendResult(sendResult);
        }
        catch (OperationCanceledException exception)
            when (cancellationToken.IsCancellationRequested)
        {
            var diagnostic = PublicChatDeliveryClassifier.PostBoundaryInterruption(
                exception
            );
            await RecordPostBoundaryInterruptionAsync(message, diagnostic);
            LogFailure(LogLevel.Warning, message, "Ambiguous", diagnostic);
            throw;
        }
        catch (Exception exception)
        {
            outcome = PublicChatDeliveryClassifier.ClassifyPostBoundaryFailure(
                exception,
                cancellationToken
            );
        }

        await RecordOutcomeAsync(message, outcome, CancellationToken.None);
    }

    private async Task ReleaseClaimAfterCancellationAsync(
        PublicChatClaimedMessage message
    )
    {
        try
        {
            _ = await ApplyClaimUpdateAsync(
                () => outbox.ReleaseClaimAsync(message, CancellationToken.None),
                CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            log.LogError(
                "Releasing canceled public chat outbox claim {OutboxMessageId} failed with {FailureType}; lease recovery will return the unsent row to pending.",
                message.Id,
                exception.GetType().FullName ?? exception.GetType().Name
            );
        }
    }

    private async Task RecordPostBoundaryInterruptionAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Send diagnostic
    )
    {
        try
        {
            _ = await ApplyClaimUpdateAsync(
                () =>
                    outbox.RecordPostBoundaryInterruptionAsync(
                        message,
                        diagnostic,
                        UtcNow(),
                        CancellationToken.None
                    ),
                CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            log.LogError(
                "Recording interrupted public chat send {OutboxMessageId} failed with {FailureType}; sending-lease recovery will retain it as ambiguous.",
                message.Id,
                exception.GetType().FullName ?? exception.GetType().Name
            );
        }
    }

    private async Task RecordOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        var recorded = await ApplyClaimUpdateAsync(
            () =>
                outbox.RecordDeliveryOutcomeAsync(
                    message,
                    outcome,
                    UtcNow(),
                    cancellationToken
                ),
            cancellationToken
        );
        if (recorded is PublicChatClaimUpdate.OwnershipLost)
        {
            log.LogWarning(
                "Public chat outbox message {OutboxMessageId} lost claim ownership while recording {OutcomeType}.",
                message.Id,
                outcome.GetType().Name
            );
        }

        LogOutcome(message, outcome);
    }

    private void LogOutcome(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome
    ) =>
        outcome.Match(
            static _ => { },
            transient =>
                LogFailure(
                    LogLevel.Warning,
                    message,
                    "SafePreSendTransient",
                    transient.Diagnostic
                ),
            rejection =>
                log.LogWarning(
                    "Twitch rejected public chat outbox message {OutboxMessageId} in #{Channel} with code {RejectionCode}.",
                    message.Id,
                    message.Channel,
                    rejection.Reason.Match(code => code.Value, () => "Unspecified")
                ),
            ambiguous =>
                LogFailure(
                    LogLevel.Warning,
                    message,
                    "Ambiguous",
                    ambiguous.Diagnostic
                ),
            unexpected =>
                LogFailure(
                    LogLevel.Error,
                    message,
                    "Unexpected",
                    unexpected.Diagnostic
                )
        );

    private void LogFailure(
        LogLevel level,
        PublicChatClaimedMessage message,
        string classification,
        PublicChatFailureDiagnostic diagnostic
    )
    {
        var phase = diagnostic.Match(_ => "Preparation", _ => "Send");
        var status = diagnostic.HttpStatus.Match(
            code => code.Value.ToString(CultureInfo.InvariantCulture),
            () => "Unavailable"
        );
        log.Log(
            level,
            "Public chat outbox message {OutboxMessageId} in #{Channel} classified as {Classification} during {Phase} with {FailureType} and HTTP status {HttpStatusCode}.",
            message.Id,
            message.Channel,
            classification,
            phase,
            diagnostic.FailureType.Value,
            status
        );
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

    private async Task WaitForSignalOrScheduledWakeAsync(
        TimeSpan delay,
        CancellationToken cancellationToken
    )
    {
        if (delay <= TimeSpan.Zero || wakeSignals.Reader.TryRead(out _))
            return;

        var scheduledWakeSignals = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropWrite,
            }
        );
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        using var wakeTimer = timeProvider.CreateTimer(
            static state =>
            {
                _ = ((ChannelWriter<bool>)state!).TryWrite(true);
            },
            scheduledWakeSignals.Writer,
            delay,
            Timeout.InfiniteTimeSpan
        );
        var scheduledWake = scheduledWakeSignals.Reader
            .ReadAsync(waitCancellation.Token)
            .AsTask();
        var signal = wakeSignals.Reader.ReadAsync(waitCancellation.Token).AsTask();
        var completed = await Task.WhenAny(scheduledWake, signal);
        await waitCancellation.CancelAsync();
        _ = await completed;
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
                    await WaitForSignalOrScheduledWakeAsync(
                        ClaimContentionDelay,
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
