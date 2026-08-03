using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DurableAlertTests
{
    [Test]
    public async Task ActiveAndAcknowledgedAlerts_LoadingState_SeparatesActiveAndHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var alerts = new DurableAlertService(dbFactory, clock, TestEventBus.Create<AppEventKind>());

        _ = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "queue",
                "active",
                "Queue delayed",
                "Outbound messages are delayed.",
                "/alerts"
            )
            .RunAsync(CancellationToken.None);
        var acknowledged = await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Info,
                "setup",
                "history",
                "Setup notice",
                "Setup changed.",
                null
            )
            .RunAsync(CancellationToken.None);
        _ = await alerts
            .Acknowledge(hostId, acknowledged.Alert.Id, "moderator")
            .RunAsync(CancellationToken.None);

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
    public void SelectedHostRole_CheckingAcknowledgePermission_MatchesOperatorCapability(
        AuthRole role,
        bool expected
    )
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
    public async Task RepeatedPublicChatQueueIncident_Observing_PersistsAndNotifiesOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.QueueAlertChanged"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var alerts = new DurableAlertService(dbFactory, clock, events);
        var observer = new DurablePublicChatQueueAlertObserver(
            dbFactory,
            alerts,
            NullLogger<DurablePublicChatQueueAlertObserver>.Instance
        );
        var backlog = new PublicChatQueueBacklog(
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
        notificationCount.ShouldBe(1);
    }

    [Test]
    public async Task LaterPublicChatQueueIncident_Observing_CreatesNewAlertAndNotification()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        _ = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        var events = TestEventBus.Create<AppEventKind>();
        var notificationCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.AlertsChanged,
            ObserverIdentity.Named("Test.QueueAlertChanged"),
            (_, _) =>
            {
                notificationCount++;
                return ValueTask.CompletedTask;
            }
        );
        var alerts = new DurableAlertService(dbFactory, clock, events);
        var observer = new DurablePublicChatQueueAlertObserver(
            dbFactory,
            alerts,
            NullLogger<DurablePublicChatQueueAlertObserver>.Instance
        );

        await observer.QueueBackedUpAsync(
            new PublicChatQueueBacklog("streamer", 3, TimeSpan.FromSeconds(31), clock.GetUtcNow()),
            CancellationToken.None
        );
        await observer.QueueBackedUpAsync(
            new PublicChatQueueBacklog(
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
        notificationCount.ShouldBe(2);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
