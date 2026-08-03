using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;

namespace BlokeBot.Core.Tests;

internal static class PublicChatIntegrationTestSupport
{
    public static PublicChatRetryPolicy StandardRetryPolicy { get; } =
        CreateRetryPolicy(
            3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            DelayBackoffType.Exponential
        );

    public static PublicChatTerminalRetentionPolicy StandardRetentionPolicy { get; } =
        new() { Duration = TimeSpan.FromDays(7) };

    public static PublicChatDeliveryLifetimePolicy StandardLifetimePolicy { get; } =
        new() { MaximumAge = TimeSpan.FromSeconds(30) };

    public static PublicChatMessageQueue CreateQueue(
        IPublicChatOutbox outbox,
        IPublicChatTransport transport,
        TimeProvider timeProvider,
        BotOptions? options = null,
        IEnumerable<IPublicChatQueueAlertObserver>? observers = null,
        IEnumerable<IPublicChatTerminalRejectionObserver>? rejectionObservers = null,
        ILogger<PublicChatMessageQueue>? logger = null
    ) =>
        new(
            BotSettings.FromOptions(options ?? new BotOptions()),
            timeProvider,
            new PublicChatQueueBacklogMonitor(),
            new PublicChatQueueAlertDispatcher(
                observers ?? [],
                TestObserverFanOut.FailOnObserverFailure<
                    PublicChatQueueAlertObserverBoundary,
                    PublicChatQueueBacklog,
                    PublicChatQueueAlertDeadLetter
                >(BotObserverBoundaries.PublicChatQueueAlerts)
            ),
            outbox,
            transport,
            logger ?? NullLogger<PublicChatMessageQueue>.Instance,
            new PublicChatTerminalRejectionDispatcher(
                rejectionObservers ?? [],
                TestObserverFanOut.FailOnObserverFailure<
                    PublicChatTerminalRejectionObserverBoundary,
                    PublicChatTerminalRejection,
                    PublicChatTerminalRejectionDeadLetter
                >(BotObserverBoundaries.PublicChatTerminalRejections)
            )
        );

    public static async Task StopAsync(CancellationTokenSource stopping, Task worker)
    {
        await stopping.CancelAsync();
        await worker;
    }

    public static DateTimeOffset Utc(int hour, int minute, int second) =>
        new(2026, 7, 12, hour, minute, second, TimeSpan.Zero);

    public static PublicChatEnqueueCommand Command(string channel, string message) =>
        new()
        {
            Channel = channel,
            Message = message,
            Deadline = new PublicChatDeliveryDeadline.ConfiguredMaximum(),
        };

    public static PublicChatPreparedSend Prepared(PublicChatClaimedMessage message) =>
        new()
        {
            Message = message,
            AppAccessToken = "app-token",
            BroadcasterId = "broadcaster-id",
            BotUserId = "bot-user-id",
        };

    public static PublicChatRetryPolicy CreateRetryPolicy(
        int attemptLimit,
        TimeSpan delay,
        TimeSpan maximumDelay,
        DelayBackoffType delayBackoffType
    ) =>
        new()
        {
            AttemptLimit = attemptLimit,
            Delay = delay,
            MaximumDelay = maximumDelay,
            DelayBackoffType = delayBackoffType,
        };
}

internal sealed class RecordingPublicChatLogger<TCategory> : ILogger<TCategory>
{
    public List<PublicChatLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(static pair => pair.Key, static pair => pair.Value)
            : [];
        Entries.Add(new(logLevel, formatter(state, exception), exception, properties));
    }
}

internal sealed record PublicChatLogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties
);

internal sealed class RecordingPublicChatTransport : IPublicChatTransport
{
    private readonly Channel<PublicChatClaimedMessage> _deliveries =
        Channel.CreateUnbounded<PublicChatClaimedMessage>();
    private int _deliveryCount;

    public int DeliveryCount => Volatile.Read(ref _deliveryCount);

    public ValueTask<PublicChatPreparationOutcome> PrepareAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<PublicChatPreparationOutcome>(
            new PublicChatPreparationOutcome.Ready
            {
                Send = PublicChatIntegrationTestSupport.Prepared(message),
            }
        );
    }

    public ValueTask<PublicChatTransportSendResult> SendAsync(
        PublicChatPreparedSend prepared,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = Interlocked.Increment(ref _deliveryCount);
        return !_deliveries.Writer.TryWrite(prepared.Message)
            ? throw new InvalidOperationException("The public chat delivery could not be observed.")
            : ValueTask.FromResult<PublicChatTransportSendResult>(
                new PublicChatTransportSendResult.Sent()
            );
    }

    public ValueTask<PublicChatClaimedMessage> ReadAsync() => _deliveries.Reader.ReadAsync();
}

internal sealed class ScriptedPublicChatTransport(
    Func<
        PublicChatClaimedMessage,
        CancellationToken,
        ValueTask<PublicChatPreparationOutcome>
    > prepare,
    Func<PublicChatPreparedSend, CancellationToken, ValueTask<PublicChatTransportSendResult>> send
) : IPublicChatTransport
{
    private int _prepareCount;
    private int _sendCount;

    public int PrepareCount => Volatile.Read(ref _prepareCount);

    public int SendCount => Volatile.Read(ref _sendCount);

    public ValueTask<PublicChatPreparationOutcome> PrepareAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        _ = Interlocked.Increment(ref _prepareCount);
        return prepare(message, cancellationToken);
    }

    public ValueTask<PublicChatTransportSendResult> SendAsync(
        PublicChatPreparedSend prepared,
        CancellationToken cancellationToken
    )
    {
        _ = Interlocked.Increment(ref _sendCount);
        return send(prepared, cancellationToken);
    }
}

internal sealed class CompletionObservingPublicChatOutbox(IPublicChatOutbox inner)
    : IPublicChatOutbox
{
    private readonly Channel<PublicChatClaimedMessage> _deliveries =
        Channel.CreateUnbounded<PublicChatClaimedMessage>();
    private readonly Channel<PublicChatDeliveryOutcome> _outcomes =
        Channel.CreateUnbounded<PublicChatDeliveryOutcome>();
    private readonly Channel<PublicChatClaimOutcome> _claims =
        Channel.CreateUnbounded<PublicChatClaimOutcome>();

    public ValueTask<PublicChatEnqueueOutcome> EnqueueAsync(
        PublicChatOutboxBatch batch,
        CancellationToken cancellationToken
    ) => inner.EnqueueAsync(batch, cancellationToken);

    public async ValueTask<PublicChatClaimOutcome> TryClaimNextAsync(
        DateTimeOffset now,
        DateTimeOffset claimExpiresAt,
        TimeSpan sendInterval,
        TimeSpan duplicateCooldown,
        CancellationToken cancellationToken
    )
    {
        var outcome = await inner.TryClaimNextAsync(
            now,
            claimExpiresAt,
            sendInterval,
            duplicateCooldown,
            cancellationToken
        );
        return !_claims.Writer.TryWrite(outcome)
            ? throw new InvalidOperationException("The public chat claim could not be observed.")
            : outcome;
    }

    public ValueTask<PublicChatClaimUpdate> BeginSendAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset sendStartedAt,
        DateTimeOffset claimExpiresAt,
        CancellationToken cancellationToken
    ) => inner.BeginSendAsync(message, sendStartedAt, claimExpiresAt, cancellationToken);

    public async ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    )
    {
        var result = await inner.RecordDeliveryOutcomeAsync(
            message,
            outcome,
            recordedAt,
            cancellationToken
        );
        if (result is PublicChatClaimUpdate.Applied)
        {
            if (!_outcomes.Writer.TryWrite(outcome))
            {
                throw new InvalidOperationException(
                    "The public chat outcome could not be observed."
                );
            }

            outcome.Match(
                _ => NotifyDelivery(message),
                static _ => { },
                static _ => { },
                static _ => { },
                static _ => { },
                static _ => { },
                static _ => { },
                static _ => { }
            );
        }

        return result;
    }

    public ValueTask<PublicChatClaimUpdate> RecordPostBoundaryInterruptionAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Send diagnostic,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken
    ) =>
        inner.RecordPostBoundaryInterruptionAsync(
            message,
            diagnostic,
            interruptedAt,
            cancellationToken
        );

    public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken
    ) => inner.ReleaseClaimAsync(message, releasedAt, cancellationToken);

    public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) => inner.LoadOutstandingAsync(now, cancellationToken);

    public ValueTask<PublicChatClaimedMessage> ReadDeliveryAsync() =>
        _deliveries.Reader.ReadAsync();

    public ValueTask<PublicChatDeliveryOutcome> ReadOutcomeAsync() => _outcomes.Reader.ReadAsync();

    public ValueTask<PublicChatClaimOutcome> ReadClaimOutcomeAsync() => _claims.Reader.ReadAsync();

    private void NotifyDelivery(PublicChatClaimedMessage message)
    {
        if (!_deliveries.Writer.TryWrite(message))
        {
            throw new InvalidOperationException(
                "The public chat completion could not be observed."
            );
        }
    }
}

internal sealed class BlockingBeginSendPublicChatOutbox(IPublicChatOutbox inner) : IPublicChatOutbox
{
    private readonly Channel<PublicChatClaimedMessage> _beginAttempts =
        Channel.CreateUnbounded<PublicChatClaimedMessage>();
    private readonly Channel<bool> _beginPermission = Channel.CreateUnbounded<bool>();

    public ValueTask<PublicChatEnqueueOutcome> EnqueueAsync(
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
        if (!_beginAttempts.Writer.TryWrite(message))
        {
            throw new InvalidOperationException("The begin-send attempt could not be observed.");
        }

        _ = await _beginPermission.Reader.ReadAsync(cancellationToken);
        return await inner.BeginSendAsync(
            message,
            sendStartedAt,
            claimExpiresAt,
            cancellationToken
        );
    }

    public ValueTask<PublicChatClaimUpdate> RecordDeliveryOutcomeAsync(
        PublicChatClaimedMessage message,
        PublicChatDeliveryOutcome outcome,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken
    ) => inner.RecordDeliveryOutcomeAsync(message, outcome, recordedAt, cancellationToken);

    public ValueTask<PublicChatClaimUpdate> RecordPostBoundaryInterruptionAsync(
        PublicChatClaimedMessage message,
        PublicChatFailureDiagnostic.Send diagnostic,
        DateTimeOffset interruptedAt,
        CancellationToken cancellationToken
    ) =>
        inner.RecordPostBoundaryInterruptionAsync(
            message,
            diagnostic,
            interruptedAt,
            cancellationToken
        );

    public ValueTask<PublicChatClaimUpdate> ReleaseClaimAsync(
        PublicChatClaimedMessage message,
        DateTimeOffset releasedAt,
        CancellationToken cancellationToken
    ) => inner.ReleaseClaimAsync(message, releasedAt, cancellationToken);

    public ValueTask<IReadOnlyList<PublicChatPendingMessage>> LoadOutstandingAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) => inner.LoadOutstandingAsync(now, cancellationToken);

    public ValueTask<PublicChatClaimedMessage> ReadBeginAttemptAsync() =>
        _beginAttempts.Reader.ReadAsync();
}

internal sealed class ManualTestTimeProvider(DateTimeOffset initialNow) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly Channel<bool> _timerRegistrations = Channel.CreateUnbounded<bool>();
    private int _timerRegistrationCount;
    private int _observedTimerRegistrationCount;
    private bool _waitingForTimerRegistration;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _currentNowLocked;
        }
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
        _ = timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _currentNowLocked = _currentNowLocked.Add(delta);
            due = _timers.Where(timer => timer.IsDue(_currentNowLocked)).ToList();
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public ValueTask<bool> WaitForTimerRegistrationAsync()
    {
        lock (_gate)
        {
            if (_timerRegistrationCount > _observedTimerRegistrationCount)
            {
                _observedTimerRegistrationCount = _timerRegistrationCount;
                return ValueTask.FromResult(true);
            }

            if (_waitingForTimerRegistration)
            {
                throw new InvalidOperationException("Only one timer observer is supported.");
            }

            _waitingForTimerRegistration = true;
            return _timerRegistrations.Reader.ReadAsync();
        }
    }

    private void AddTimer(ManualTimer timer)
    {
        lock (_gate)
        {
            if (!_timers.Contains(timer))
            {
                _timers.Add(timer);
                _timerRegistrationCount++;
            }
            if (!_waitingForTimerRegistration)
            {
                return;
            }

            _waitingForTimerRegistration = false;
            _observedTimerRegistrationCount = _timerRegistrationCount;
            if (!_timerRegistrations.Writer.TryWrite(true))
            {
                throw new InvalidOperationException("The timer observer could not be notified.");
            }
        }
    }

    private void RemoveTimer(ManualTimer timer)
    {
        lock (_gate)
        {
            _ = _timers.Remove(timer);
        }
    }

    private DateTimeOffset _currentNowLocked { get; set; } = initialNow;

    private sealed class ManualTimer(
        ManualTestTimeProvider owner,
        TimerCallback callback,
        object? state
    ) : ITimer
    {
        private TimeSpan _period;
        private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAt =
                    dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner._currentNowLocked.Add(dueTime);
                owner.AddTimer(this);
            }

            if (dueTime != Timeout.InfiniteTimeSpan && dueTime <= TimeSpan.Zero)
            {
                Fire();
            }

            return true;
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
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
            lock (owner._gate)
            {
                return !_disposed && _dueAt <= value;
            }
        }

        public void Fire()
        {
            lock (owner._gate)
            {
                if (_disposed || _dueAt > owner._currentNowLocked)
                {
                    return;
                }

                if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
                {
                    _dueAt = owner._currentNowLocked.Add(_period);
                }
                else
                {
                    _disposed = true;
                    owner.RemoveTimer(this);
                }
            }

            callback(state);
        }
    }
}
