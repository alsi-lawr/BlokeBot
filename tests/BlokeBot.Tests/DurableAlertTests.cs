using BlokeBot.Auth.Sessions;
using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Hosts;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class DurableAlertTests
{
    [Test]
    public async Task ActiveAndAcknowledgedAlerts_LoadingState_SeparatesActiveAndHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());

        await alerts.CreateAsync(
            hostId,
            DurableAlertSeverity.Warning,
            "queue",
            "active",
            "Queue delayed",
            "Outbound messages are delayed.",
            "/alerts",
            CancellationToken.None
        );
        var acknowledged = await alerts.CreateAsync(
            hostId,
            DurableAlertSeverity.Info,
            "setup",
            "history",
            "Setup notice",
            "Setup changed.",
            null,
            CancellationToken.None
        );
        await alerts.AcknowledgeAsync(hostId, acknowledged.Id, "moderator", CancellationToken.None);

        var state = await alerts.LoadStateAsync(hostId, CancellationToken.None);

        state.Active.Count.ShouldBe(1);
        state.Active[0].Title.ShouldBe("Queue delayed");
        state.ActiveCount.ShouldBe(1);
        state.History.Count.ShouldBe(1);
        state.History[0].Title.ShouldBe("Setup notice");
        state.History[0].AcknowledgedByLogin.ShouldBe("moderator");
        state.History[0].AcknowledgedAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
    }

    [Test]
    [Arguments(AuthRole.Streamer, true)]
    [Arguments(AuthRole.Moderator, true)]
    [Arguments(AuthRole.Admin, true)]
    [Arguments(AuthRole.Bot, false)]
    public void SelectedHostRole_CheckingAcknowledgePermission_MatchesOperatorCapability(AuthRole role, bool expected)
    {
        var selectedHost = new BotHostChoice(42, "streamer", "Streamer", role);
        var principal = TestPrincipals.BlokeBotUser(
            login: "actor",
            role: role,
            isBotAccount: role == AuthRole.Bot,
            availableHosts: [selectedHost],
            selectedHost: selectedHost
        );
        var session = AuthenticatedSession.FromPrincipal(principal);

        DurableAlertPermissions.CanAcknowledge(session).ShouldBe(expected);
    }

    [Test]
    public void SessionWithoutSelectedHost_CheckingAcknowledgePermission_ReturnsFalse()
    {
        var principal = TestPrincipals.BlokeBotUser("operator", canCreateHost: true);
        var session = AuthenticatedSession.FromPrincipal(principal);

        DurableAlertPermissions.CanAcknowledge(session).ShouldBeFalse();
    }

    [Test]
    public async Task RepeatedQueueIncident_Observing_PersistsAndNotifiesOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());
        var subscriber = new RecordingAlertSubscriber();
        var observer = new DurableOutboundQueueAlertObserver(
            dbFactory,
            alerts,
            Dispatcher(subscriber),
            NullLogger<DurableOutboundQueueAlertObserver>.Instance
        );
        var backlog = new TwitchOutboundQueueBacklog(
            "streamer",
            3,
            TimeSpan.FromSeconds(31),
            clock.GetUtcNow()
        );

        await observer.QueueBackedUpAsync(backlog, CancellationToken.None);
        await observer.QueueBackedUpAsync(backlog, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.DurableAlerts.SingleAsync(CancellationToken.None);
        stored.HostId.ShouldBe(hostId);
        stored.Severity.ShouldBe(DurableAlertSeverity.Warning);
        stored.Source.ShouldBe("twitch-outbound-queue");
        stored.SourceKey.ShouldStartWith("streamer:");
        stored.AcknowledgedAtUtc.ShouldBeNull();
        subscriber.Notifications.Count.ShouldBe(1);
        subscriber.Notifications[0].AlertId.ShouldBe(stored.Id);
        subscriber.Notifications[0].HostId.ShouldBe(hostId);
        subscriber.Notifications[0].HostLogin.ShouldBe("streamer");
    }

    [Test]
    public async Task LaterQueueIncident_Observing_CreatesNewAlertAndNotification()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());
        var subscriber = new RecordingAlertSubscriber();
        var observer = new DurableOutboundQueueAlertObserver(
            dbFactory,
            alerts,
            Dispatcher(subscriber),
            NullLogger<DurableOutboundQueueAlertObserver>.Instance
        );

        await observer.QueueBackedUpAsync(
            new TwitchOutboundQueueBacklog(
                "streamer",
                3,
                TimeSpan.FromSeconds(31),
                clock.GetUtcNow()
            ),
            CancellationToken.None
        );
        await observer.QueueBackedUpAsync(
            new TwitchOutboundQueueBacklog(
                "streamer",
                2,
                TimeSpan.FromSeconds(31),
                clock.GetUtcNow().AddMinutes(10)
            ),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var alertsCreated = await db.DurableAlerts.ToArrayAsync(CancellationToken.None);
        alertsCreated.Length.ShouldBe(2);
        alertsCreated.Select(x => x.SourceKey).Distinct().Count().ShouldBe(2);
        subscriber.Notifications.Count.ShouldBe(2);
    }

    [Test]
    public async Task FailingAlertSubscriber_ObservingQueueIncident_PersistsAndNotifiesOthers()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());
        var recording = new RecordingAlertSubscriber();
        var observer = new DurableOutboundQueueAlertObserver(
            dbFactory,
            alerts,
            Dispatcher(new ThrowingAlertSubscriber(), recording),
            NullLogger<DurableOutboundQueueAlertObserver>.Instance
        );

        await observer.QueueBackedUpAsync(
            new TwitchOutboundQueueBacklog(
                "streamer",
                3,
                TimeSpan.FromSeconds(31),
                clock.GetUtcNow()
            ),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.DurableAlerts.CountAsync()).ShouldBe(1);
        recording.Notifications.Count.ShouldBe(1);
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static OutboundQueueAlertSubscriberDispatcher Dispatcher(
        params IOutboundQueueAlertSubscriber[] subscribers
    ) =>
        new(
            subscribers,
            NullLogger<OutboundQueueAlertSubscriberDispatcher>.Instance
        );

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

    private sealed class ThrowingAlertSubscriber : IOutboundQueueAlertSubscriber
    {
        public Task AlertCreatedAsync(
            OutboundQueueAlertNotification notification,
            CancellationToken ct
        ) => throw new InvalidOperationException("Subscriber failed.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
