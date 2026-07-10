using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class OutboundMessageQueueTests
{
    [Test]
    public void Split_prefers_line_sentence_then_word_breaks()
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
    public async Task Long_messages_are_split_and_sent_in_order()
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
    public async Task Duplicate_messages_wait_without_blocking_different_messages()
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
    public async Task Stuck_queue_alerts_once_per_backup_and_resets_after_drain()
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
    public void Duplicate_cooldown_expires_at_boundary_and_prunes_stale_entries()
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
    public void Backlog_monitor_tracks_channels_independently_and_resets_after_drain()
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
    public async Task Alert_dispatcher_contains_observer_failure()
    {
        var recording = new RecordingQueueAlertObserver();
        var dispatcher = new TwitchOutboundQueueAlertDispatcher(
            [new ThrowingQueueAlertObserver(), recording],
            NullLogger<TwitchOutboundQueueAlertDispatcher>.Instance
        );
        var alert = new TwitchOutboundQueueBacklog(
            "channel",
            2,
            TimeSpan.FromSeconds(5),
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );

        await dispatcher.NotifyAsync([alert]);

        recording.Alerts.ShouldBe([alert]);
    }

    private static TwitchOutboundMessageQueue CreateQueue(
        TwitchBotOptions options,
        TimeProvider? timeProvider = null,
        IEnumerable<ITwitchOutboundQueueAlertObserver>? observers = null
    ) =>
        new(
            Options.Create(options),
            timeProvider ?? TimeProvider.System,
            new TwitchOutboundDuplicateCooldown(),
            new TwitchOutboundQueueBacklogMonitor(),
            new TwitchOutboundQueueAlertDispatcher(
                observers ?? [],
                NullLogger<TwitchOutboundQueueAlertDispatcher>.Instance
            ),
            NullLogger<TwitchOutboundMessageQueue>.Instance
        );

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

    private sealed class ThrowingQueueAlertObserver : ITwitchOutboundQueueAlertObserver
    {
        public ValueTask QueueBackedUpAsync(
            TwitchOutboundQueueBacklog backlog,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Observer failed.");
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = initialNow;
        private TaskCompletionSource? timerWaiter;

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

        public Task WaitForTimerRegistrationAsync()
        {
            lock (gate)
            {
                if (timers.Count > 0)
                    return Task.CompletedTask;

                timerWaiter = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                return timerWaiter.Task;
            }
        }

        private void AddTimer(ManualTimer timer)
        {
            lock (gate)
            {
                if (!timers.Contains(timer))
                    timers.Add(timer);
                timerWaiter?.TrySetResult();
                timerWaiter = null;
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
