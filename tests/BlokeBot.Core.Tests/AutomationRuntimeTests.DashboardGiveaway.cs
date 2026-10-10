using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task DashboardGiveaway_ActualNativeStartDrawCancel_AdmitLifecycleAutomationEffects()
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations | HostFeatureFlags.Points
        );
        f.Clock.Advance(DateTimeOffset.UtcNow - f.Clock.GetUtcNow());
        _ = await SaveExpandedChatAsync(
            f,
            "giveaway-lifecycle",
            """{"event":"GiveawayOpened"}""",
            "opened"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "giveaway-lifecycle",
            """{"event":"GiveawayClosed"}""",
            "closed"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "giveaway-lifecycle",
            """{"event":"GiveawayWinners"}""",
            "winners"
        );
        await using (var db = f.Database.CreateDbContext())
        {
            _ = db.PointsSettings.Add(
                new PointsSettings
                {
                    HostId = f.HostId,
                    GiveawayCooldownSeconds = 0,
                    GiveawayMinimumPayout = "10",
                    GiveawayMaximumPayout = "10",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var expanded = ExpandedFixtureFor(f);
        var lifecycle = new AutomationFeatureLifecycle(
            () => expanded.Runtime,
            NullLogger<AutomationFeatureLifecycle>.Instance
        );
        var scheduler = new GiveawayLifecycleTestServices.Scheduler();
        var giveaways = GiveawayLifecycleTestServices.Create(f.Database, scheduler, lifecycle);
        var controls = new DashboardGiveawayControls(
            new(f.Database, new DashboardControlModerator()),
            f.Database,
            giveaways
        );
        var session = DashboardSession(f.HostId);
        DashboardHostId host = new(f.HostId);
        var configuration = new DashboardWidgetConfiguration.Giveaway(
            new DashboardBinding.Configured(new DashboardTarget.Giveaway())
        );

        _ = Operation(
                await controls.StartAsync(session, host, configuration, CancellationToken.None)
            )
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["opened"]);
        _ = await giveaways.JoinAsync(
            f.HostId,
            "streamer",
            "viewer",
            new Dictionary<string, string>(),
            CancellationToken.None
        );
        _ = Operation(await controls.EndAsync(session, host, configuration, CancellationToken.None))
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Skip(1).Order().ShouldBe(["closed", "winners"]);
        _ = Operation(await controls.EndAsync(session, host, configuration, CancellationToken.None))
            .ShouldBeOfType<PointOperationOutcome.Failed>();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(3);
        _ = Operation(
                await controls.StartAsync(session, host, configuration, CancellationToken.None)
            )
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        _ = Operation(
                await controls.CancelAsync(session, host, configuration, CancellationToken.None)
            )
            .ShouldBeOfType<PointOperationOutcome.Succeeded>();
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Skip(3).ShouldBe(["opened", "closed"]);
        scheduler.Scheduled.Count.ShouldBe(2);
        scheduler.Cancelled.ShouldBe(scheduler.Scheduled.Select(x => x.GiveawayId));
        await using var final = f.Database.CreateDbContext();
        (await final.AutomationFlowRuns.CountAsync()).ShouldBe(5);
        (await final.PointLedgerEntries.CountAsync()).ShouldBe(1);

        static PointOperationOutcome Operation(
            DashboardResult<DashboardGiveawayResult<PointOperationOutcome>> result
        ) =>
            result
                .ShouldBeOfType<DashboardResult<
                    DashboardGiveawayResult<PointOperationOutcome>
                >.Succeeded>()
                .Value.ShouldBeOfType<DashboardGiveawayResult<PointOperationOutcome>.Ready>()
                .Value;
    }
}

internal sealed class GiveawayLifecycleTestServices : PointsGiveawaySchedulerTestBase
{
    internal sealed class Scheduler : IPointsGiveawayScheduler
    {
        internal List<PointsGiveawaySchedule> Scheduled { get; } = [];
        internal List<int> Cancelled { get; } = [];

        public void Schedule(PointsGiveawaySchedule schedule) => Scheduled.Add(schedule);

        public void Cancel(int giveawayId) => Cancelled.Add(giveawayId);
    }

    internal static PointsGiveawayService Create(
        IDbContextFactory<BlokeBotDbContext> database,
        IPointsGiveawayScheduler scheduler,
        AutomationFeatureLifecycle lifecycle
    )
    {
        var status = new HostBotStatusService(
            new StaticHostBotAppAccessTokenSource(),
            new UnavailableHostBotAccountTokenStatusProvider(),
            new HelixClient(
                new FakeHttpClientFactory(streamIsLive: true),
                TwitchEndpointPolicy.Default
            ),
            BotSettings.FromOptions(new BotOptions())
        );
        var draws = new PointsGiveawayDrawService(
            database,
            new PointBalanceService(database),
            new FixedPointsRandom(),
            [],
            lifecycle
        );
        return new(
            database,
            draws,
            new(status, NullLogger<PointsGiveawayEligibilityPolicy>.Instance),
            new PointsGiveawayMessageFormatter(),
            scheduler,
            new(new PointsChangeNotifier(TestEventBus.Create<AppEventKind>())),
            lifecycle
        );
    }
}
