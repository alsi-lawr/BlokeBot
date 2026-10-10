using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class DashboardGiveawayControlsTests
{
    [Test]
    public async Task DashboardGiveaway_StartJoinDrawAndCancel_UseNativeSettingsPayoutAndNotifications()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SettingsAsync(s =>
        {
            s.GiveawayDurationSeconds = 123;
            s.GiveawayMinimumPayout = "20";
            s.GiveawayMaximumPayout = "20";
        });
        var notifications = new List<PointsGiveawayCurrent?>();
        var unrelated = 0;
        var pointsChanged = 0;
        using var points = f.Events.Subscribe(
            AppEventKind.PointsChanged,
            ObserverIdentity.Named("points"),
            (_, _) =>
            {
                pointsChanged++;
                return ValueTask.CompletedTask;
            }
        );
        using var other = f.Changes.Subscribe(
            f.Other,
            ObserverIdentity.Named("other"),
            (_, _) =>
            {
                unrelated++;
                return ValueTask.CompletedTask;
            }
        );
        using var refresh = f.Changes.Subscribe(
            f.Host,
            ObserverIdentity.Named("widget"),
            async (_, ct) =>
                notifications.Add(
                    Ready(await f.Controls.ReadAsync(f.Owner, f.Host, _configuration, ct))
                )
        );

        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        var schedule = f.Scheduler.Scheduled.ShouldHaveSingleItem();
        schedule.HostLogin.ShouldBe("streamer");
        (schedule.EndsAtUtc - schedule.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(123));
        schedule.Reply.ShouldBeNull();
        f.Provider.RequestedLogins.ShouldHaveSingleItem().ShouldContain("streamer");
        f.Provider.RequestedLogins.Single().ShouldNotContain("stale-login");
        _ = (
            await f.Giveaways.JoinAsync(
                f.Host.Value,
                "streamer",
                "viewer",
                new Dictionary<string, string>(),
                _ct
            )
        ).ShouldBeOfType<PointOperationOutcome.Succeeded>();
        notifications.Select(x => x?.EntrantCount).ShouldBe([0, 1]);

        _ = Ready(await f.Controls.EndAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        _ = Ready(await f.Controls.EndAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        f.Scheduler.Cancelled.ShouldBe([schedule.GiveawayId]);
        notifications.Last().ShouldBeNull();
        await using (var db = f.Database.CreateDbContext())
        {
            (await db.PointsGiveaways.SingleAsync()).Status.ShouldBe(
                PointsGiveawayStatus.Completed
            );
            (await db.PointBalances.SingleAsync()).Amount.ShouldBe("20");
            (await db.PointLedgerEntries.CountAsync()).ShouldBe(1);
            (await db.PointsGiveawayWinners.CountAsync()).ShouldBe(1);
        }
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        _ = Ready(await f.Controls.CancelAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        f.Scheduler.Cancelled.ShouldBe(f.Scheduler.Scheduled.Select(x => x.GiveawayId));
        pointsChanged.ShouldBe(5);
        unrelated.ShouldBe(0);
        notifications.Count.ShouldBe(5);
        refresh.Dispose();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        notifications.Count.ShouldBe(5);
        await using var final = f.Database.CreateDbContext();
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(1);
        (
            await final.PointsGiveaways.CountAsync(x => x.Status == PointsGiveawayStatus.Cancelled)
        ).ShouldBe(1);
    }

    [Test]
    public async Task DashboardGiveaway_AuthorityChangesAfterRead_RejectAllOperationsWithoutMutation()
    {
        await using var f = await Fixture.CreateAsync();
        var moderator = Session(f.Host, AuthRole.Moderator);
        Ready(await f.Controls.ReadAsync(moderator, f.Host, _configuration, _ct)).ShouldBeNull();
        foreach (
            var outcome in new ModeratorAuthorityOutcome[]
            {
                new ModeratorAuthorityOutcome.Revoked(),
                new ModeratorAuthorityOutcome.Unavailable(),
            }
        )
        {
            f.Moderator.Outcome = outcome;
            _ = (
                await f.Controls.ReadAsync(moderator, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointsGiveawayCurrent?>
            >.Rejected>();
            _ = (
                await f.Controls.StartAsync(moderator, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointOperationOutcome>
            >.Rejected>();
            _ = (
                await f.Controls.EndAsync(moderator, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointOperationOutcome>
            >.Rejected>();
            _ = (
                await f.Controls.CancelAsync(moderator, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointOperationOutcome>
            >.Rejected>();
        }
        foreach (var actor in new[] { Session(f.Other), Session(f.Host, AuthRole.Bot) })
        {
            _ = (
                await f.Controls.StartAsync(actor, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointOperationOutcome>
            >.Rejected>();
            _ = (
                await f.Controls.EndAsync(actor, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointOperationOutcome>
            >.Rejected>();
            _ = (
                await f.Controls.CancelAsync(actor, f.Host, _configuration, _ct)
            ).ShouldBeOfType<DashboardResult<
                DashboardGiveawayResult<PointOperationOutcome>
            >.Rejected>();
        }
        f.Scheduler.Scheduled.ShouldBeEmpty();
        f.Scheduler.Cancelled.ShouldBeEmpty();
        f.Provider.RequestedLogins.ShouldBeEmpty();
        await using var db = f.Database.CreateDbContext();
        (await db.PointsGiveaways.CountAsync()).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
        f.Moderator.Outcome = new ModeratorAuthorityOutcome.Granted();
        _ = Ready(await f.Controls.StartAsync(moderator, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
    }

    [Test]
    public async Task DashboardGiveaway_CurrentFeatureAndSettingsReadiness_RetainsBindingWithoutCreatingSettings()
    {
        await using var f = await Fixture.CreateAsync();
        await f.FeaturesAsync(HostFeatureFlags.None);
        await AssertUnavailableAsync(_configuration, DashboardBindingAvailability.FeatureDisabled);
        await f.FeaturesAsync(HostFeatureFlags.All);
        await using (var db = f.Database.CreateDbContext())
        {
            _ = await db.PointsSettings.Where(x => x.HostId == f.Host.Value).ExecuteDeleteAsync();
        }
        await AssertUnavailableAsync(_configuration, DashboardBindingAvailability.Missing);
        var unconfigured = new DashboardWidgetConfiguration.Giveaway(
            new DashboardBinding.Unconfigured(DashboardBindingIssue.Missing, "imported target")
        );
        await AssertUnavailableAsync(unconfigured, DashboardBindingAvailability.Unconfigured);
        await using var final = f.Database.CreateDbContext();
        (await final.PointsSettings.CountAsync(x => x.HostId == f.Host.Value)).ShouldBe(0);
        (await final.PointsGiveaways.CountAsync()).ShouldBe(0);
        f.Scheduler.Scheduled.ShouldBeEmpty();
        f.Provider.RequestedLogins.ShouldBeEmpty();

        async Task AssertUnavailableAsync(
            DashboardWidgetConfiguration.Giveaway configuration,
            DashboardBindingAvailability reason
        )
        {
            (await f.Controls.ReadAsync(f.Owner, f.Host, configuration, _ct))
                .ShouldBeOfType<DashboardResult<
                    DashboardGiveawayResult<PointsGiveawayCurrent?>
                >.Succeeded>()
                .Value.ShouldBeOfType<DashboardGiveawayResult<PointsGiveawayCurrent?>.Unavailable>()
                .Reason.ShouldBe(reason);
            foreach (
                var result in new[]
                {
                    await f.Controls.StartAsync(f.Owner, f.Host, configuration, _ct),
                    await f.Controls.EndAsync(f.Owner, f.Host, configuration, _ct),
                    await f.Controls.CancelAsync(f.Owner, f.Host, configuration, _ct),
                }
            )
            {
                result
                    .ShouldBeOfType<DashboardResult<
                        DashboardGiveawayResult<PointOperationOutcome>
                    >.Succeeded>()
                    .Value.ShouldBeOfType<DashboardGiveawayResult<PointOperationOutcome>.Unavailable>()
                    .Reason.ShouldBe(reason);
            }
        }
    }

    [Test]
    public async Task DashboardGiveaway_ConflictCooldownAndEligibilityFailures_KeepNativeStateAndSchedule()
    {
        await using var f = await Fixture.CreateAsync();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        _ = Ready(await f.Controls.CancelAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        await f.SettingsAsync(s => s.GiveawayCooldownSeconds = 300);
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        await f.SettingsAsync(s => s.GiveawayCooldownSeconds = 0);
        f.Provider.Live = false;
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        f.Provider.Live = true;
        f.Provider.Unavailable = true;
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        f.Provider.Unavailable = false;
        await f.SettingsAsync(s => s.GiveawayEligibility = PointsEligibilityMode.Followers);
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        f.Scheduler.Scheduled.Count.ShouldBe(1);
        f.Scheduler.Cancelled.Count.ShouldBe(1);
        Ready(await f.Controls.ReadAsync(f.Owner, f.Host, _configuration, _ct)).ShouldBeNull();
        await using var db = f.Database.CreateDbContext();
        (await db.PointsGiveaways.CountAsync()).ShouldBe(1);
        (await db.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DashboardGiveaway_PayoutFailure_RollsBackEarlierWinnerAndLeavesActiveSchedule()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SettingsAsync(s => s.GiveawayWinnerCount = 2);
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        _ = await f.Giveaways.JoinAsync(
            f.Host.Value,
            "streamer",
            "first",
            new Dictionary<string, string>(),
            _ct
        );
        _ = await f.Giveaways.JoinAsync(
            f.Host.Value,
            "streamer",
            "capped",
            new Dictionary<string, string>(),
            _ct
        );
        await using (var db = f.Database.CreateDbContext())
        {
            _ = db.PointBalances.Add(
                new PointBalance
                {
                    HostId = f.Host.Value,
                    Login = "capped",
                    Amount = PointAmount.MaximumValue.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        _ = Ready(await f.Controls.EndAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        Ready(await f.Controls.ReadAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldNotBeNull()
            .EntrantCount.ShouldBe(2);
        f.Scheduler.Cancelled.ShouldBeEmpty();
        await using var final = f.Database.CreateDbContext();
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(0);
        (await final.PointsGiveawayWinners.CountAsync()).ShouldBe(0);
        (await final.PointBalances.CountAsync()).ShouldBe(1);
        (await final.PointsGiveaways.SingleAsync()).Status.ShouldBe(PointsGiveawayStatus.Active);
    }

    [Test]
    public async Task DashboardGiveaway_ExternalJoinAndScheduledDraw_RefreshHostWidgetFromNativeWorker()
    {
        await using var f = await Fixture.CreateAsync();
        var refreshes = new List<PointsGiveawayCurrent?>();
        using var subscription = f.Changes.Subscribe(
            f.Host,
            ObserverIdentity.Named("widget"),
            async (_, ct) =>
                refreshes.Add(
                    Ready(await f.Controls.ReadAsync(f.Owner, f.Host, _configuration, ct))
                )
        );
        _ = Ready(await f.Controls.StartAsync(f.Owner, f.Host, _configuration, _ct));
        _ = await f.Giveaways.JoinAsync(
            f.Host.Value,
            "streamer",
            "scheduledviewer",
            new Dictionary<string, string>(),
            _ct
        );
        var schedule = f.Scheduler.Scheduled.Single();
        using var worker = f.Worker(new AutoAdvanceTimeProvider(new(schedule.StartedAtUtc)));
        await worker.ExecuteScheduleAsync(schedule, _ct);
        refreshes.Select(x => x?.EntrantCount).ShouldBe([0, 1, null]);
        await using var db = f.Database.CreateDbContext();
        (await db.PointsGiveaways.SingleAsync()).Status.ShouldBe(PointsGiveawayStatus.Completed);
        (await db.PointLedgerEntries.SingleAsync()).Login.ShouldBe("scheduledviewer");
    }

    [Test]
    public async Task DashboardGiveaway_CompactRead_CountsInSqlWithoutMaterializingEntrantsOrHistory()
    {
        await using var f = await Fixture.CreateAsync();
        await AssertCompactReadAsync(f);
    }

    [Test]
    [Explicit]
    public async Task DashboardGiveaway_PostgreSqlCompactRead_CountsInSqlWithoutMaterializingEntrantsOrHistory()
    {
        await using var f = await Fixture.CreateAsync(postgres: true);
        await AssertCompactReadAsync(f);
    }

    private static async Task AssertCompactReadAsync(Fixture f)
    {
        await f.SeedVolumeAsync(300, 1000);
        var current = Ready(await f.Controls.ReadAsync(f.Owner, f.Host, _configuration, _ct))
            .ShouldNotBeNull();
        current.EntrantCount.ShouldBe(1000);
        _ = current.Lifecycle.ShouldBeOfType<PointsGiveawayLifecycle.Active>();
        f.Queries.Entities.ShouldBeEmpty();
        f.Queries.Commands.Count.ShouldBeLessThanOrEqualTo(3);
        f.Queries.ReaderOperations.Sum().ShouldBeLessThanOrEqualTo(6);
        var commands = string.Join("\n---\n", f.Queries.Commands);
        commands.ShouldContain("COUNT(", Case.Insensitive);
        commands.ShouldNotContain("point_ledger");
        commands.ShouldNotContain("point_balances");
        commands.ShouldNotContain("points_giveaway_winners");
        var output = Environment.GetEnvironmentVariable("BLOKEBOT_DASHBOARD_GIVEAWAY_SQL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            await File.WriteAllTextAsync(output, commands);
        }
        f.Queries.Reset();
        Ready(await f.Controls.ReadAsync(Session(f.Other), f.Other, _configuration, _ct))
            .ShouldNotBeNull()
            .EntrantCount.ShouldBe(7);
        f.Queries.Entities.ShouldBeEmpty();
    }
}
