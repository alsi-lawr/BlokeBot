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
    public async Task Load_state_returns_active_alerts_and_acknowledgement_history()
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
    public void Selected_host_operator_roles_can_acknowledge_alerts(AuthRole role, bool expected)
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
    public void Users_without_selected_host_cannot_acknowledge_alerts()
    {
        var principal = TestPrincipals.BlokeBotUser("operator", canCreateHost: true);
        var session = AuthenticatedSession.FromPrincipal(principal);

        DurableAlertPermissions.CanAcknowledge(session).ShouldBeFalse();
    }

    [Test]
    public async Task Outbound_queue_observer_persists_alert_once_when_whisper_sender_skips()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());
        var whispers = new RecordingWhisperSender();
        var observer = new DurableOutboundQueueAlertObserver(
            dbFactory,
            alerts,
            whispers,
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
        whispers.Requests.Count.ShouldBe(1);
        whispers.Requests[0].HostId.ShouldBe(hostId);
        whispers.Requests[0].HostLogin.ShouldBe("streamer");
    }

    [Test]
    public async Task Outbound_queue_observer_creates_new_alert_for_later_backup_incident()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());
        var whispers = new RecordingWhisperSender();
        var observer = new DurableOutboundQueueAlertObserver(
            dbFactory,
            alerts,
            whispers,
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
        whispers.Requests.Count.ShouldBe(2);
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

    private sealed class RecordingWhisperSender : IOutboundQueueAlertWhisperSender
    {
        public List<OutboundQueueAlertWhisperRequest> Requests { get; } = [];

        public Task TrySendAsync(OutboundQueueAlertWhisperRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
