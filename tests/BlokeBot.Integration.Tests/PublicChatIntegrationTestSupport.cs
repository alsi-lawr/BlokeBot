using System.Threading.Channels;
using BlokeBot.Eventing;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlokeBot.Integration.Tests;

internal static class PublicChatIntegrationTestSupport
{
    public static PublicChatMessageQueue CreateQueue(
        IPublicChatOutbox outbox,
        IPublicChatTransport transport,
        TimeProvider timeProvider,
        TwitchBotOptions? options = null,
        IEnumerable<IPublicChatQueueAlertObserver>? observers = null
    ) =>
        new(
            TwitchBotSettings.FromOptions(options ?? new TwitchBotOptions()),
            timeProvider,
            new PublicChatQueueBacklogMonitor(),
            new PublicChatQueueAlertDispatcher(
                observers ?? [],
                TestObserverFanOut.Continue<
                    PublicChatQueueAlertObserverBoundary,
                    PublicChatQueueBacklog,
                    PublicChatQueueAlertDeadLetter
                >(TwitchBotObserverBoundaries.PublicChatQueueAlerts)
            ),
            outbox,
            transport,
            NullLogger<PublicChatMessageQueue>.Instance
        );

    public static async Task StopAsync(
        CancellationTokenSource stopping,
        Task worker
    )
    {
        await stopping.CancelAsync();
        await worker;
    }

    public static DateTimeOffset Utc(int hour, int minute, int second) =>
        new(2026, 7, 12, hour, minute, second, TimeSpan.Zero);

    public static PublicChatEnqueueCommand Command(
        string channel,
        string message
    ) =>
        new() { Channel = channel, Message = message };
}

internal sealed class RecordingPublicChatTransport : IPublicChatTransport
{
    private readonly Channel<PublicChatClaimedMessage> deliveries =
        Channel.CreateUnbounded<PublicChatClaimedMessage>();
    private int deliveryCount;

    public int DeliveryCount => Volatile.Read(ref deliveryCount);

    public ValueTask SendAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref deliveryCount);
        if (!deliveries.Writer.TryWrite(message))
        {
            throw new InvalidOperationException(
                "The public chat delivery could not be observed."
            );
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<PublicChatClaimedMessage> ReadAsync() =>
        deliveries.Reader.ReadAsync();
}

internal sealed class CompletionObservingPublicChatOutbox(IPublicChatOutbox inner)
    : IPublicChatOutbox
{
    private readonly Channel<PublicChatClaimedMessage> deliveries =
        Channel.CreateUnbounded<PublicChatClaimedMessage>();

    public ValueTask<PublicChatOutboxReceipt> EnqueueAsync(
        PublicChatOutboxBatch batch,
        CancellationToken cancellationToken
    ) => inner.EnqueueAsync(batch, cancellationToken);

    public ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset claimExpiresAt,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown,
        CancellationToken cancellationToken
    ) =>
        inner.TryClaimNextAsync(
            now,
            claimExpiresAt,
            sendInterval,
            duplicateCooldown,
            cancellationToken
        );

    public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken
    ) =>
        inner.BeginSendAsync(
            message,
            sendStartedAt,
            claimExpiresAt,
            cancellationToken
        );

    public async ValueTask<PublicChatClaimUpdate> MarkDeliveredAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken
    )
    {
        var result = await inner.MarkDeliveredAsync(
            message,
            deliveredAt,
            cancellationToken
        );
        if (
            result is PublicChatClaimUpdate.Applied
            && !deliveries.Writer.TryWrite(message)
        )
        {
            throw new InvalidOperationException(
                "The public chat completion could not be observed."
            );
        }

        return result;
    }

    public ValueTask<PublicChatClaimUpdate> MarkFaultedAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset faultedAt,
        CancellationToken cancellationToken
    ) => inner.MarkFaultedAsync(message, faultedAt, cancellationToken);

    public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    ) => inner.ReleaseClaimAsync(message, cancellationToken);

    public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        CancellationToken cancellationToken
    ) => inner.LoadOutstandingAsync(cancellationToken);

    public ValueTask<PublicChatClaimedMessage> ReadDeliveryAsync() =>
        deliveries.Reader.ReadAsync();
}

internal sealed class BlockingBeginSendPublicChatOutbox(IPublicChatOutbox inner)
    : IPublicChatOutbox
{
    private readonly Channel<PublicChatClaimedMessage> beginAttempts =
        Channel.CreateUnbounded<PublicChatClaimedMessage>();
    private readonly Channel<bool> beginPermission = Channel.CreateUnbounded<bool>();

    public ValueTask<PublicChatOutboxReceipt> EnqueueAsync(
        PublicChatOutboxBatch batch,
        CancellationToken cancellationToken
    ) => inner.EnqueueAsync(batch, cancellationToken);

    public ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset claimExpiresAt,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown,
        CancellationToken cancellationToken
    ) =>
        inner.TryClaimNextAsync(
            now,
            claimExpiresAt,
            sendInterval,
            duplicateCooldown,
            cancellationToken
        );

    public async ValueTask<PublicChatClaimUpdate> BeginSendAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken
    )
    {
        if (!beginAttempts.Writer.TryWrite(message))
            throw new InvalidOperationException("The begin-send attempt could not be observed.");

        _ = await beginPermission.Reader.ReadAsync(cancellationToken);
        return await inner.BeginSendAsync(
            message,
            sendStartedAt,
            claimExpiresAt,
            cancellationToken
        );
    }

    public ValueTask<PublicChatClaimUpdate> MarkDeliveredAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken
    ) => inner.MarkDeliveredAsync(message, deliveredAt, cancellationToken);

    public ValueTask<PublicChatClaimUpdate> MarkFaultedAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset faultedAt,
        CancellationToken cancellationToken
    ) => inner.MarkFaultedAsync(message, faultedAt, cancellationToken);

    public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    ) => inner.ReleaseClaimAsync(message, cancellationToken);

    public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        CancellationToken cancellationToken
    ) => inner.LoadOutstandingAsync(cancellationToken);

    public ValueTask<PublicChatClaimedMessage> ReadBeginAttemptAsync() =>
        beginAttempts.Reader.ReadAsync();
}

internal sealed class ManualTestTimeProvider(DateTimeOffset initialNow) : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];
    private readonly Channel<bool> timerRegistrations = Channel.CreateUnbounded<bool>();
    private DateTimeOffset now = initialNow;
    private bool waitingForTimerRegistration;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
            return now;
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        List<ManualTimer> due;
        lock (gate)
        {
            now = now.Add(delta);
            due = timers.Where(timer => timer.IsDue(now)).ToList();
        }

        foreach (var timer in due)
            timer.Fire();
    }

    public ValueTask<bool> WaitForTimerRegistrationAsync()
    {
        lock (gate)
        {
            if (timers.Count > 0)
                return ValueTask.FromResult(true);

            if (waitingForTimerRegistration)
                throw new InvalidOperationException("Only one timer observer is supported.");

            waitingForTimerRegistration = true;
            return timerRegistrations.Reader.ReadAsync();
        }
    }

    private void AddTimer(ManualTimer timer)
    {
        lock (gate)
        {
            if (!timers.Contains(timer))
                timers.Add(timer);
            if (!waitingForTimerRegistration)
                return;

            waitingForTimerRegistration = false;
            if (!timerRegistrations.Writer.TryWrite(true))
            {
                throw new InvalidOperationException(
                    "The timer observer could not be notified."
                );
            }
        }
    }

    private void RemoveTimer(ManualTimer timer)
    {
        lock (gate)
            timers.Remove(timer);
    }

    private DateTimeOffset CurrentNowLocked => now;

    private sealed class ManualTimer(
        ManualTestTimeProvider owner,
        TimerCallback callback,
        object? state
    ) : ITimer
    {
        private TimeSpan period;
        private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
        private bool disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (disposed)
                    return false;

                this.period = period;
                dueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : owner.CurrentNowLocked.Add(dueTime);
                owner.AddTimer(this);
            }

            if (dueTime != Timeout.InfiniteTimeSpan && dueTime <= TimeSpan.Zero)
                Fire();

            return true;
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed)
                    return;

                disposed = true;
                owner.RemoveTimer(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public bool IsDue(DateTimeOffset value)
        {
            lock (owner.gate)
                return !disposed && dueAt <= value;
        }

        public void Fire()
        {
            lock (owner.gate)
            {
                if (disposed || dueAt > owner.CurrentNowLocked)
                    return;

                if (period > TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                    dueAt = owner.CurrentNowLocked.Add(period);
                else
                {
                    disposed = true;
                    owner.RemoveTimer(this);
                }
            }

            callback(state);
        }
    }
}
