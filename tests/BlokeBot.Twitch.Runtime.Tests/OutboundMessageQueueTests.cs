using System.Threading.Channels;
using BlokeBot.Twitch.Runtime;
using BlokeBot.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class OutboundMessageQueueTests
{
    [Test]
    public void MessageWithMultipleBreakTypes_Splitting_PrefersLineSentenceThenWord()
    {
        TwitchChatMessageSplitter
            .Split("first line\nsecond line", 12)
            .ShouldBe(["first line", "second line"]);

        TwitchChatMessageSplitter
            .Split("First sentence. Second one.", 20)
            .ShouldBe(["First sentence.", "Second one."]);

        TwitchChatMessageSplitter.Split("alpha beta gamma", 10).ShouldBe(["alpha", "beta gamma"]);
    }

    [Test]
    public async Task MessageOverLength_Queueing_SplitsAndSendsInOrder()
    {
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 0,
                MaxChatMessageLength = 10,
            }
        );
        List<string> sent = [];

        await queue.SendAsync(
            "channel",
            "alpha beta gamma",
            (message, _) =>
            {
                sent.Add(message.Message);
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        sent.ShouldBe(["alpha", "beta gamma"]);
    }

    [Test]
    public async Task DuplicateAndDistinctMessages_Queueing_DelaysOnlyDuplicate()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 0,
                DuplicateChatMessageCooldownSeconds = 1,
            },
            clock
        );
        List<string> sent = [];

        await queue.SendAsync("channel", "same", SendAsync, CancellationToken.None);
        var duplicate = queue.SendAsync("channel", "same", SendAsync, CancellationToken.None);
        var different = queue.SendAsync("channel", "different", SendAsync, CancellationToken.None);

        await different;
        duplicate.IsCompleted.ShouldBeFalse();

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await duplicate;
        sent.ShouldBe(["same", "different", "same"]);
        return;

        Task SendAsync(TwitchOutboundChatMessage message, CancellationToken _)
        {
            sent.Add(message.Message);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task RepeatedAndLaterBackups_MonitoringQueue_AlertsOncePerIncident()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var observer = new RecordingQueueAlertObserver();
        var queue = CreateQueue(
            new TwitchBotOptions
            {
                ChatMessageSendIntervalSeconds = 10,
                DuplicateChatMessageCooldownSeconds = 0,
                OutboundQueueAlerts = new TwitchOutboundQueueAlertOptions
                {
                    StuckAfterSeconds = 5,
                },
            },
            clock,
            [observer]
        );
        List<string> sent = [];

        await queue.SendAsync("channel", "first", SendAsync, CancellationToken.None);
        var second = queue.SendAsync("channel", "second", SendAsync, CancellationToken.None);
        var third = queue.SendAsync("channel", "third", SendAsync, CancellationToken.None);

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await observer.WaitForCountAsync(1);

        observer.Alerts[0].Channel.ShouldBe("channel");
        observer.Alerts[0].OldestPendingAge.ShouldBe(TimeSpan.FromSeconds(5));
        observer.Alerts[0].PendingCount.ShouldBeGreaterThanOrEqualTo(1);

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await second;
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        await third;
        observer.Alerts.Count.ShouldBe(1);

        var fourth = queue.SendAsync("channel", "fourth", SendAsync, CancellationToken.None);
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await observer.WaitForCountAsync(2);
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await fourth;

        sent.ShouldBe(["first", "second", "third", "fourth"]);
        return;

        Task SendAsync(TwitchOutboundChatMessage message, CancellationToken _)
        {
            sent.Add(message.Message);
            return Task.CompletedTask;
        }
    }

    [Test]
    public void DuplicateCooldownAtBoundary_CheckingNextAllowed_PrunesStaleEntries()
    {
        var cooldown = new TwitchOutboundDuplicateCooldown();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var first = new TwitchOutboundChatMessage("first", "same");
        var second = new TwitchOutboundChatMessage("second", "same");
        cooldown.RecordSent(first, now, TimeSpan.FromSeconds(5));
        cooldown.RecordSent(second, now, TimeSpan.FromSeconds(5));
        cooldown.EntryCount.ShouldBe(2);

        cooldown
            .NextAllowedAt(first, now.AddSeconds(4), TimeSpan.FromSeconds(5))
            .ShouldBe(now.AddSeconds(5));
        cooldown
            .NextAllowedAt(first, now.AddSeconds(5), TimeSpan.FromSeconds(5))
            .ShouldBe(now.AddSeconds(5));
        cooldown.EntryCount.ShouldBe(0);
    }

    [Test]
    public void BacklogsAcrossChannels_CapturingAlerts_TracksIndependentlyAndResetsDrained()
    {
        var monitor = new TwitchOutboundQueueBacklogMonitor();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 10, TimeSpan.Zero);
        TwitchOutboundPendingState[] pending =
        [
            new("first", now.AddSeconds(-10)),
            new("first", now.AddSeconds(-6)),
            new("second", now.AddSeconds(-7)),
        ];

        var firstIncidents = monitor.CaptureAlerts(
            pending,
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        firstIncidents.Select(x => x.Channel).ShouldBe(["first", "second"]);
        monitor
            .CaptureAlerts(pending, now, TimeSpan.FromSeconds(5), enabled: true)
            .ShouldBeEmpty();

        monitor.ResetDrainedChannels([new("second", now)]);
        var nextFirstIncident = monitor.CaptureAlerts(
            [new("first", now.AddSeconds(-5)), new("second", now.AddSeconds(-5))],
            now,
            TimeSpan.FromSeconds(5),
            enabled: true
        );
        nextFirstIncident.Select(x => x.Channel).ShouldBe(["first"]);
    }

    [Test]
    public async Task FailingQueueAlertObserver_DispatchingAlert_NotifiesRemainingObservers()
    {
        var recording = new RecordingQueueAlertObserver();
        var dispatcher = new TwitchOutboundQueueAlertDispatcher(
            [new ThrowingQueueAlertObserver("Observer failed."), recording],
            QueueAlertFanOut()
        );
        var alert = new TwitchOutboundQueueBacklog(
            "channel",
            2,
            TimeSpan.FromSeconds(5),
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );

        await dispatcher.NotifyAsync([alert], CancellationToken.None);

        recording.Alerts.ShouldBe([alert]);
    }

    [Test]
    public async Task AlertHandlingEscalation_ProcessingQueue_SendsPendingAndLaterMessages()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var logger = new RecordingLogger<TwitchOutboundMessageQueue>();
        var queue = new TwitchOutboundMessageQueue(
            TwitchBotSettings.FromOptions(
                new TwitchBotOptions
                {
                    ChatMessageSendIntervalSeconds = 10,
                    DuplicateChatMessageCooldownSeconds = 0,
                    OutboundQueueAlerts = new TwitchOutboundQueueAlertOptions
                    {
                        StuckAfterSeconds = 5,
                    },
                }
            ),
            clock,
            new TwitchOutboundDuplicateCooldown(),
            new TwitchOutboundQueueBacklogMonitor(),
            new TwitchOutboundQueueAlertDispatcher(
                [new ThrowingQueueAlertObserver("observer secret payload")],
                RuntimeTestObserverFanOut.EscalatingContinue<
                    TwitchOutboundQueueAlertObserverBoundary,
                    TwitchOutboundQueueBacklog,
                    TwitchOutboundQueueAlertDeadLetter
                >(
                    TwitchBotObserverBoundaries.OutboundQueueAlerts,
                    new IOException("reporter secret payload")
                )
            ),
            logger
        );
        var sent = new List<string>();

        await queue.SendAsync("channel", "first", SendAsync, CancellationToken.None);
        var second = queue.SendAsync(
            "channel",
            "second secret chat payload",
            SendAsync,
            CancellationToken.None
        );
        second.IsCompleted.ShouldBeFalse();
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        await second;

        var third = queue.SendAsync("channel", "third", SendAsync, CancellationToken.None);
        third.IsCompleted.ShouldBeFalse();
        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(10));
        await third;

        sent.ShouldBe(["first", "second secret chat payload", "third"]);
        logger.Entries.Count.ShouldBe(2);
        foreach (var entry in logger.Entries)
        {
            entry.Level.ShouldBe(LogLevel.Error);
            entry.Exception.ShouldBeNull();
            entry.Message.ShouldContain("Continuing queued chat processing");
            entry.Message.ShouldContain(TwitchBotObserverBoundaries.OutboundQueueAlerts.Value);
            entry.Message.ShouldContain(nameof(ObserverFailureHandlingStage.Reporter));
            entry.Message.ShouldNotContain("observer secret payload");
            entry.Message.ShouldNotContain("reporter secret payload");
            entry.Message.ShouldNotContain("second secret chat payload");
        }
        return;

        Task SendAsync(TwitchOutboundChatMessage message, CancellationToken _)
        {
            sent.Add(message.Message);
            return Task.CompletedTask;
        }
    }

    private static TwitchOutboundMessageQueue CreateQueue(
        TwitchBotOptions options,
        TimeProvider? timeProvider = null,
        IEnumerable<ITwitchOutboundQueueAlertObserver>? observers = null
    ) =>
        new(
            TwitchBotSettings.FromOptions(options),
            timeProvider ?? TimeProvider.System,
            new TwitchOutboundDuplicateCooldown(),
            new TwitchOutboundQueueBacklogMonitor(),
            new TwitchOutboundQueueAlertDispatcher(
                observers ?? [],
                QueueAlertFanOut()
            ),
            NullLogger<TwitchOutboundMessageQueue>.Instance
        );

    private static ObserverFanOut<
        TwitchOutboundQueueAlertObserverBoundary,
        TwitchOutboundQueueBacklog,
        TwitchOutboundQueueAlertDeadLetter
    > QueueAlertFanOut() =>
        RuntimeTestObserverFanOut.Continue<
            TwitchOutboundQueueAlertObserverBoundary,
            TwitchOutboundQueueBacklog,
            TwitchOutboundQueueAlertDeadLetter
        >(TwitchBotObserverBoundaries.OutboundQueueAlerts);

    private sealed class RecordingQueueAlertObserver : ITwitchOutboundQueueAlertObserver
    {
        private readonly object gate = new();
        private TaskCompletionSource? waiter;
        private int waiterTarget;

        public List<TwitchOutboundQueueBacklog> Alerts { get; } = [];

        public ValueTask QueueBackedUpAsync(
            TwitchOutboundQueueBacklog backlog,
            CancellationToken cancellationToken
        )
        {
            lock (gate)
            {
                Alerts.Add(backlog);
                if (Alerts.Count >= waiterTarget)
                    waiter?.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public Task WaitForCountAsync(int count)
        {
            lock (gate)
            {
                if (Alerts.Count >= count)
                    return Task.CompletedTask;

                waiterTarget = count;
                waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return waiter.Task;
            }
        }
    }

    private sealed class ThrowingQueueAlertObserver(string failureMessage)
        : ITwitchOutboundQueueAlertObserver
    {
        public ValueTask QueueBackedUpAsync(
            TwitchOutboundQueueBacklog backlog,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException(failureMessage);
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception
    );

    private sealed class ManualTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private readonly Channel<bool> timerRegistrations =
            Channel.CreateUnbounded<bool>();
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
                {
                    throw new InvalidOperationException(
                        "Only one timer-registration observer is supported."
                    );
                }

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
                        "The timer-registration observer could not be notified."
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
            ManualTimeProvider owner,
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
                    dueAt =
                        dueTime == Timeout.InfiniteTimeSpan
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
                    {
                        dueAt = owner.CurrentNowLocked.Add(period);
                    }
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
}
