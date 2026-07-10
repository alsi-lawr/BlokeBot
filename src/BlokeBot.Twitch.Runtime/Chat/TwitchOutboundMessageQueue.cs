using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchOutboundMessageQueue(
    IOptions<TwitchBotOptions> options,
    TimeProvider timeProvider,
    IEnumerable<ITwitchOutboundQueueAlertObserver> alertObservers,
    ILogger<TwitchOutboundMessageQueue> log
)
{
    private readonly object gate = new();
    private readonly ITwitchOutboundQueueAlertObserver[] alertObservers = alertObservers.ToArray();
    private readonly HashSet<string> backedUpAlertChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TwitchOutboundMessageKey, DateTimeOffset> lastSent = [];
    private readonly List<PendingMessage> pending = [];
    private TaskCompletionSource processorWakeSignal = NewWakeSignal();
    private bool processing;
    private DateTimeOffset lastSendAttemptAt = DateTimeOffset.MinValue;

    public async Task SendAsync(
        string channel,
        string message,
        Func<TwitchOutboundChatMessage, CancellationToken, Task> send,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(message))
            return;

        foreach (var part in TwitchChatMessageSplitter.Split(message, MaxMessageLength))
            await EnqueueAsync(channel, part, send, cancellationToken);
    }

    private async Task EnqueueAsync(
        string channel,
        string message,
        Func<TwitchOutboundChatMessage, CancellationToken, Task> send,
        CancellationToken cancellationToken
    )
    {
        var item = new PendingMessage(
            new TwitchOutboundChatMessage(channel, message),
            send,
            UtcNow(),
            cancellationToken
        );
        using var registration = cancellationToken.Register(item.Cancel);

        lock (gate)
        {
            pending.Add(item);
            if (!processing)
            {
                processing = true;
                _ = Task.Run(ProcessAsync, CancellationToken.None);
            }
            else
            {
                processorWakeSignal.TrySetResult();
            }
        }

        await item.Task;
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            var delay = TimeSpan.Zero;
            PendingMessage? item = null;
            IReadOnlyList<TwitchOutboundQueueBacklog> queueAlerts;

            lock (gate)
            {
                pending.RemoveAll(x => x.IsCanceled);
                ResetDrainedAlertChannelsLocked();
                if (pending.Count == 0)
                {
                    processing = false;
                    return;
                }

                var now = UtcNow();
                queueAlerts = CaptureQueueAlertsLocked(now);
                (item, delay) = NextPendingMessage(now);
                if (item is not null)
                {
                    pending.Remove(item);
                    ResetDrainedAlertChannelsLocked();
                }
                else if (NextQueueAlertDelayLocked(now) is { } alertDelay)
                {
                    delay = delay <= TimeSpan.Zero ? alertDelay : Min(delay, alertDelay);
                }
            }

            await NotifyQueueAlertsAsync(queueAlerts);

            if (item is null)
            {
                await WaitForDelayOrNewMessageAsync(delay);
                continue;
            }

            await SendPendingAsync(item);
        }
    }

    private async Task WaitForDelayOrNewMessageAsync(TimeSpan delay)
    {
        Task wakeTask;
        lock (gate)
            wakeTask = processorWakeSignal.Task;

        var completed = await Task.WhenAny(Task.Delay(delay, timeProvider), wakeTask);
        if (completed != wakeTask)
            return;

        lock (gate)
        {
            if (ReferenceEquals(wakeTask, processorWakeSignal.Task))
                processorWakeSignal = NewWakeSignal();
        }
    }

    private (PendingMessage? Message, TimeSpan Delay) NextPendingMessage(DateTimeOffset now)
    {
        var nextMessage = pending
            .Select(message => new PendingCandidate(message, NextSendAt(message, now)))
            .OrderBy(candidate => candidate.SendAt)
            .ThenBy(candidate => pending.IndexOf(candidate.Message))
            .First();

        return nextMessage.SendAt <= now
            ? (nextMessage.Message, TimeSpan.Zero)
            : (null, nextMessage.SendAt - now);
    }

    private DateTimeOffset NextSendAt(PendingMessage item, DateTimeOffset now)
    {
        var nextSendAt = lastSendAttemptAt + SendInterval;
        var key = TwitchOutboundMessageKey.From(item.Message);
        if (DuplicateCooldown > TimeSpan.Zero && lastSent.TryGetValue(key, out var lastSentAt))
            nextSendAt = Max(nextSendAt, lastSentAt + DuplicateCooldown);

        return Max(nextSendAt, now);
    }

    private async Task SendPendingAsync(PendingMessage item)
    {
        if (item.IsCanceled)
            return;

        try
        {
            await item.Send(item.Message, item.CancellationToken);
            var now = UtcNow();
            lock (gate)
            {
                lastSendAttemptAt = now;
                lastSent[TwitchOutboundMessageKey.From(item.Message)] = now;
            }
            item.Complete();
        }
        catch (Exception ex)
        {
            lock (gate)
                lastSendAttemptAt = UtcNow();

            log.LogWarning(
                ex,
                "Twitch chat message send failed for #{Channel}.",
                item.Message.Channel
            );
            item.Fail(ex);
        }
    }

    private TimeSpan SendInterval =>
        TimeSpan.FromSeconds(Math.Max(0, options.Value.ChatMessageSendIntervalSeconds));

    private TimeSpan DuplicateCooldown =>
        TimeSpan.FromSeconds(Math.Max(0, options.Value.DuplicateChatMessageCooldownSeconds));

    private TimeSpan QueueStuckThreshold =>
        TimeSpan.FromSeconds(Math.Max(0, options.Value.OutboundQueueAlerts.StuckAfterSeconds));

    private int MaxMessageLength => Math.Max(0, options.Value.MaxChatMessageLength);

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private DateTimeOffset UtcNow() => timeProvider.GetUtcNow();

    private IReadOnlyList<TwitchOutboundQueueBacklog> CaptureQueueAlertsLocked(
        DateTimeOffset now
    )
    {
        if (alertObservers.Length == 0)
            return [];

        var threshold = QueueStuckThreshold;
        if (threshold <= TimeSpan.Zero)
            return [];

        List<TwitchOutboundQueueBacklog>? alerts = null;
        foreach (var group in PendingByChannelLocked())
        {
            if (backedUpAlertChannels.Contains(group.Channel))
                continue;

            var oldest = group.Messages.MinBy(x => x.EnqueuedAt);
            if (oldest is null)
                continue;

            var age = now - oldest.EnqueuedAt;
            if (age < threshold)
                continue;

            backedUpAlertChannels.Add(group.Channel);
            alerts ??= [];
            alerts.Add(
                new TwitchOutboundQueueBacklog(
                    group.Channel,
                    group.Messages.Count,
                    age,
                    oldest.EnqueuedAt
                )
            );
        }

        return alerts ?? [];
    }

    private TimeSpan? NextQueueAlertDelayLocked(DateTimeOffset now)
    {
        if (alertObservers.Length == 0)
            return null;

        var threshold = QueueStuckThreshold;
        if (threshold <= TimeSpan.Zero)
            return null;

        TimeSpan? next = null;
        foreach (var group in PendingByChannelLocked())
        {
            if (backedUpAlertChannels.Contains(group.Channel))
                continue;

            var oldest = group.Messages.MinBy(x => x.EnqueuedAt);
            if (oldest is null)
                continue;

            var remaining = threshold - (now - oldest.EnqueuedAt);
            if (remaining <= TimeSpan.Zero)
                return TimeSpan.Zero;

            next = next is null ? remaining : Min(next.Value, remaining);
        }

        return next;
    }

    private List<PendingChannelGroup> PendingByChannelLocked() =>
        pending
            .GroupBy(x => NormalizeChannel(x.Message.Channel), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new PendingChannelGroup(group.Key, group.ToList()))
            .ToList();

    private async Task NotifyQueueAlertsAsync(IReadOnlyList<TwitchOutboundQueueBacklog> alerts)
    {
        if (alerts.Count == 0 || alertObservers.Length == 0)
            return;

        foreach (var alert in alerts)
        {
            foreach (var observer in alertObservers)
            {
                try
                {
                    await observer.QueueBackedUpAsync(alert, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogWarning(
                        ex,
                        "Twitch outbound queue alert observer failed for #{Channel}.",
                        alert.Channel
                    );
                }
            }
        }
    }

    private void ResetDrainedAlertChannelsLocked()
    {
        if (backedUpAlertChannels.Count == 0)
            return;

        if (pending.Count == 0)
        {
            backedUpAlertChannels.Clear();
            return;
        }

        var activeChannels = pending
            .Select(x => NormalizeChannel(x.Message.Channel))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        backedUpAlertChannels.RemoveWhere(channel => !activeChannels.Contains(channel));
    }

    private static string NormalizeChannel(string channel) => channel.Trim().ToLowerInvariant();

    private static TaskCompletionSource NewWakeSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct PendingCandidate(PendingMessage Message, DateTimeOffset SendAt);

    private sealed record PendingChannelGroup(string Channel, List<PendingMessage> Messages);

    private readonly record struct TwitchOutboundMessageKey(string Channel, string Message)
    {
        public static TwitchOutboundMessageKey From(TwitchOutboundChatMessage message) =>
            new(message.Channel.Trim().ToLowerInvariant(), message.Message.Trim());
    }

    private sealed class PendingMessage
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly CancellationToken cancellationToken;

        public PendingMessage(
            TwitchOutboundChatMessage message,
            Func<TwitchOutboundChatMessage, CancellationToken, Task> send,
            DateTimeOffset enqueuedAt,
            CancellationToken cancellationToken
        )
        {
            Message = message;
            Send = send;
            EnqueuedAt = enqueuedAt;
            this.cancellationToken = cancellationToken;
        }

        public DateTimeOffset EnqueuedAt { get; }

        public CancellationToken CancellationToken => cancellationToken;

        public bool IsCanceled =>
            completion.Task.IsCanceled || cancellationToken.IsCancellationRequested;

        public TwitchOutboundChatMessage Message { get; }

        public Func<TwitchOutboundChatMessage, CancellationToken, Task> Send { get; }

        public Task Task => completion.Task;

        public void Cancel() => completion.TrySetCanceled(cancellationToken);

        public void Complete() => completion.TrySetResult();

        public void Fail(Exception ex) => completion.TrySetException(ex);
    }
}
