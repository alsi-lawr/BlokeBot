using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Integration.Tests;

public sealed class OutboundQueueAlertIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task QueueBackupWithOptionalSubscriber_DetectingIncident_PersistsAlertAndNotifiesWhenPresent(
        bool includeSubscriber
    )
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var clock = new ManualTimerTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var events = new EventBus<AppEventKind>();
        var alertCreated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            _ =>
            {
                alertCreated.TrySetResult();
                return Task.CompletedTask;
            }
        );
        var subscriber = new RecordingAlertSubscriber();
        IOutboundQueueAlertSubscriber[] subscribers = includeSubscriber ? [subscriber] : [];
        var alertService = new DurableAlertService(dbFactory, clock, events);
        var durableObserver = new DurableOutboundQueueAlertObserver(
            dbFactory,
            alertService,
            new OutboundQueueAlertSubscriberDispatcher(
                subscribers,
                NullLogger<OutboundQueueAlertSubscriberDispatcher>.Instance
            ),
            NullLogger<DurableOutboundQueueAlertObserver>.Instance
        );
        var queue = CreateQueue(clock, durableObserver);

        await queue.SendAsync("streamer", "first", SendAsync, CancellationToken.None);
        var second = queue.SendAsync("streamer", "second", SendAsync, CancellationToken.None);

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await alertCreated.Task;

        var state = await alertService.LoadStateAsync(hostId, CancellationToken.None);
        var alert = state.Active.ShouldHaveSingleItem();
        alert.Source.ShouldBe("twitch-outbound-queue");
        alert.LinkPath.ShouldBe("/alerts");
        subscriber.Notifications.Count.ShouldBe(includeSubscriber ? 1 : 0);

        await clock.WaitForTimerRegistrationAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        await second;
    }

    private static TwitchOutboundMessageQueue CreateQueue(
        TimeProvider clock,
        ITwitchOutboundQueueAlertObserver observer
    ) =>
        new(
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
                [observer],
                NullLogger<TwitchOutboundQueueAlertDispatcher>.Instance
            ),
            NullLogger<TwitchOutboundMessageQueue>.Instance
        );

    private static Task SendAsync(
        TwitchOutboundChatMessage message,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class RecordingAlertSubscriber : IOutboundQueueAlertSubscriber
    {
        public List<OutboundQueueAlertNotification> Notifications { get; } = [];

        public Task AlertCreatedAsync(
            OutboundQueueAlertNotification notification,
            CancellationToken ct
        )
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimerTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = initialNow;
        private TaskCompletionSource? timerRegistration;

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

        // This is an event barrier for CreateTimer; only Advance moves time.
        public Task WaitForTimerRegistrationAsync()
        {
            lock (gate)
            {
                if (timers.Count > 0)
                    return Task.CompletedTask;

                timerRegistration = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                return timerRegistration.Task;
            }
        }

        private void AddTimer(ManualTimer timer)
        {
            lock (gate)
            {
                if (!timers.Contains(timer))
                    timers.Add(timer);
                timerRegistration?.TrySetResult();
                timerRegistration = null;
            }
        }

        private void RemoveTimer(ManualTimer timer)
        {
            lock (gate)
                timers.Remove(timer);
        }

        private DateTimeOffset CurrentNowLocked => now;

        private sealed class ManualTimer(
            ManualTimerTimeProvider owner,
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
