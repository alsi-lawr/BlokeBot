using BlokeBot.Core.Features.BlokeRaid;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BlokeRaidServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task DuplicateConcurrentSpecial_SpendsAndRecordsOnceWithoutUnderflow()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = Service(database, clock, 10);
        await SaveConfigurationAsync(
            service,
            hostId,
            Draft() with
            {
                SpecialMinimum = 10,
                SpecialMaximum = 10,
                SpecialCooldownSeconds = 0,
                SpecialPointCost = new(75),
            }
        );
        await SeedBalanceAsync(database, hostId, "viewer", 100);
        _ = Success(await service.StartAsync(hostId, Campaign("start"), default));
        var command = new BlokeRaidActionCommand(
            "chat:duplicate-special",
            BlokeRaidActionKind.Special,
            Viewer("viewer"),
            "stream-1"
        );

        var outcomes = await Task.WhenAll(
            service.ActAsync(hostId, command, default),
            service.ActAsync(hostId, command, default)
        );

        outcomes.ShouldAllBe(outcome => outcome is BlokeRaidActionOutcome.Succeeded);
        outcomes
            .Cast<BlokeRaidActionOutcome.Succeeded>()
            .Count(outcome => !outcome.WasIdempotent)
            .ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        (await verify.BlokeRaidActions.CountAsync()).ShouldBe(1);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BlokeRaidSpecialSpend
            )
        ).ShouldBe(1);
        PointAmount
            .ParseAbsolute((await verify.PointBalances.SingleAsync()).Amount)
            .ShouldBe(new PointAmount(25));
    }

    [Test]
    public async Task ConcurrentWinningAttacks_BoundHealthAndAwardVictoryExactlyOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = Service(database, new ManualTimeProvider(_now), 100);
        await SaveConfigurationAsync(
            service,
            hostId,
            Draft() with
            {
                MaximumHealth = 100,
                AttackMinimum = 100,
                AttackMaximum = 100,
                AttackCooldownSeconds = 0,
                VictoryPointReward = new(50),
            }
        );
        _ = Success(await service.StartAsync(hostId, Campaign("start"), default));

        var outcomes = await Task.WhenAll(
            service.ActAsync(
                hostId,
                new("chat:win-one", BlokeRaidActionKind.Attack, Viewer("winner"), "stream"),
                default
            ),
            service.ActAsync(
                hostId,
                new("chat:win-two", BlokeRaidActionKind.Attack, Viewer("winner"), "stream"),
                default
            )
        );

        outcomes.Count(outcome => outcome is BlokeRaidActionOutcome.Succeeded).ShouldBe(1);
        outcomes.Count(outcome => outcome is BlokeRaidActionOutcome.NoActiveCampaign).ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        var campaign = await verify.BlokeRaidCampaigns.SingleAsync();
        campaign.Status.ShouldBe(BlokeRaidCampaignStatus.Victory);
        campaign.CurrentHealth.ShouldBe(0);
        (await verify.BlokeRaidActions.CountAsync()).ShouldBe(1);
        (
            await verify.BlokeRaidEvents.CountAsync(value =>
                value.Kind == BlokeRaidEventKind.CampaignVictorious
            )
        ).ShouldBe(1);
        (
            await verify.PointLedgerEntries.CountAsync(value =>
                value.Kind == PointLedgerKind.BlokeRaidVictoryReward
            )
        ).ShouldBe(1);
        PointAmount
            .ParseAbsolute((await verify.PointBalances.SingleAsync()).Amount)
            .ShouldBe(new PointAmount(50));
    }

    [Test]
    public async Task ViewerActions_EnforceCooldownThenPerStreamLimit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = Service(database, clock, 4);
        await SaveConfigurationAsync(
            service,
            hostId,
            Draft() with
            {
                AttackMinimum = 4,
                AttackMaximum = 4,
                AttackCooldownSeconds = 10,
                AttackPerStreamLimit = 2,
            }
        );
        _ = Success(await service.StartAsync(hostId, Campaign("start"), default));

        _ = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:first", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream-a"),
                default
            )
        );
        clock.Advance(TimeSpan.FromSeconds(5));
        _ = (
            await service.ActAsync(
                hostId,
                new("chat:cooldown", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream-a"),
                default
            )
        ).ShouldBeOfType<BlokeRaidActionOutcome.Cooldown>();
        clock.Advance(TimeSpan.FromSeconds(5));
        _ = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:second", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream-a"),
                default
            )
        );
        clock.Advance(TimeSpan.FromSeconds(10));
        _ = (
            await service.ActAsync(
                hostId,
                new("chat:limited", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream-a"),
                default
            )
        ).ShouldBeOfType<BlokeRaidActionOutcome.PerStreamLimitReached>();
        _ = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:next-stream", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream-b"),
                default
            )
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.BlokeRaidActions.CountAsync()).ShouldBe(3);
    }

    [Test]
    public async Task Disable_PreservesAndPausesWithoutReplayAcrossEveryMutationBoundary()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = Service(database, clock, 5);
        await SaveConfigurationAsync(
            service,
            hostId,
            Draft() with
            {
                ResetPolicy = BlokeRaidResetPolicy.Weekly,
                WeeklyResetDay = DayOfWeek.Wednesday,
                WeeklyResetHourUtc = 12,
            }
        );
        var started = Success(
            await service.StartAsync(hostId, Campaign("start"), default)
        ).Campaign;
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            [],
            clock
        );
        await features.DisableAsync(hostId, HostFeatureFlags.CooperativeGame, default);
        await using (var before = await database.CreateDbContextAsync())
        {
            var campaign = await before.BlokeRaidCampaigns.SingleAsync();
            campaign.PublicId.ShouldBe(started.Id);
        }
        var countsBefore = await CountsAsync(database);
        clock.Advance(TimeSpan.FromDays(8));

        _ = (
            await service.ActAsync(
                hostId,
                new("chat:suppressed", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream"),
                default
            )
        ).ShouldBeOfType<BlokeRaidActionOutcome.FeatureDisabled>();
        _ = (
            await service.ResetAsync(hostId, Campaign("suppressed-reset"), default)
        ).ShouldBeOfType<BlokeRaidCampaignOutcome.FeatureDisabled>();
        _ = (
            await service.SaveConfigurationAsync(hostId, Draft(), default)
        ).ShouldBeOfType<BlokeRaidConfigurationOutcome.FeatureDisabled>();
        _ = (
            await service.ApplyGuessingResultAsync(
                hostId,
                new(42, _now.AddDays(1), [Viewer("guesser")]),
                default
            )
        ).ShouldBeOfType<BlokeRaidActionOutcome.FeatureDisabled>();
        await service.ProcessDueWorkAsync(hostId, default);
        (await service.LoadModeratorAsync(hostId, default)).ShouldBeNull();
        (await service.LoadPublicAsync("alpha", default)).ShouldBeNull();
        (await service.LoadEventsAsync(hostId, 0, 100, default)).ShouldBeEmpty();
        (await CountsAsync(database)).ShouldBe(countsBefore);

        await features.EnableAsync(hostId, HostFeatureFlags.CooperativeGame, default);
        var resumed = await service.LoadModeratorAsync(hostId, default);
        var resumedValue = resumed.ShouldNotBeNull();
        var resumedCampaign = resumedValue.ActiveCampaign.ShouldNotBeNull();
        resumedCampaign.Id.ShouldBe(started.Id);
        resumedCampaign.EndsAtUtc.ShouldBe(started.EndsAtUtc.AddDays(8));
        resumedValue
            .Configuration.NextWeeklyResetAtUtc.ShouldNotBeNull()
            .ShouldBeGreaterThan(clock.GetUtcNow().UtcDateTime);
        _ = (
            await service.ApplyGuessingResultAsync(
                hostId,
                new(42, _now.AddDays(1), [Viewer("guesser")]),
                default
            )
        ).ShouldBeOfType<BlokeRaidActionOutcome.SourceSuppressed>();
        (await CountsAsync(database)).ShouldBe(countsBefore);
    }

    [Test]
    public async Task GuessingIntegration_IsHostScopedIdempotentAndRestartsFromRecordedOutcomes()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var alphaId = await SeedHostAsync(database, "alpha");
        var betaId = await SeedHostAsync(database, "beta");
        var clock = new ManualTimeProvider(_now);
        var service = Service(database, clock, 99);
        await SaveConfigurationAsync(service, alphaId, Draft() with { CorrectGuessDamage = 7 });
        _ = Success(await service.StartAsync(alphaId, Campaign("alpha-start"), default));
        _ = Success(await service.StartAsync(betaId, Campaign("beta-start"), default));
        var result = new BlokeRaidGuessingResult(
            184,
            _now.AddMinutes(5),
            [Viewer("one"), Viewer("two"), Viewer("one")]
        );

        var applied = ActionSuccess(
            await service.ApplyGuessingResultAsync(alphaId, result, default)
        );
        applied.Action.Outcome.ShouldBe(14);
        applied.Campaign.Contributions.Count.ShouldBe(2);
        var duplicate = ActionSuccess(
            await service.ApplyGuessingResultAsync(alphaId, result, default)
        );
        duplicate.WasIdempotent.ShouldBeTrue();

        var restarted = Service(database, clock, 1);
        var alpha = await restarted.LoadModeratorAsync(alphaId, default);
        var beta = await restarted.LoadModeratorAsync(betaId, default);
        var alphaCampaign = alpha.ShouldNotBeNull().ActiveCampaign.ShouldNotBeNull();
        alphaCampaign.RecentActions.Single().Outcome.ShouldBe(14);
        alphaCampaign.Contributions.Count.ShouldBe(2);
        var betaCampaign = beta.ShouldNotBeNull().ActiveCampaign.ShouldNotBeNull();
        betaCampaign.CurrentHealth.ShouldBe(betaCampaign.MaximumHealth);
        betaCampaign.Contributions.ShouldBeEmpty();
    }

    [Test]
    public async Task InjectedOutcome_RecordsDeterministicPhaseResponseAcrossRestart()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = Service(database, clock, 40);
        await SaveConfigurationAsync(
            service,
            hostId,
            Draft() with
            {
                MaximumHealth = 100,
                AttackMinimum = 1,
                AttackMaximum = 99,
                PhaseTwoHealthPercent = 65,
                PhaseThreeHealthPercent = 30,
                PhaseTwoResponse = "The shell splits exactly once.",
            }
        );
        _ = Success(await service.StartAsync(hostId, Campaign("start"), default));

        var action = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:phase", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream"),
                default
            )
        );

        action.Action.Outcome.ShouldBe(40);
        action.Action.PhaseAfter.ShouldBe(2);
        action.Action.Response.ShouldBe("The shell splits exactly once.");
        var restarted = await Service(database, clock, 1).LoadModeratorAsync(hostId, default);
        var restartedCampaign = restarted.ShouldNotBeNull().ActiveCampaign.ShouldNotBeNull();
        restartedCampaign.CurrentHealth.ShouldBe(60);
        restartedCampaign.CurrentPhase.ShouldBe(2);
        restartedCampaign
            .RecentActions.Single()
            .Response.ShouldBe("The shell splits exactly once.");
    }

    [Test]
    public async Task ThresholdEdit_PreservesReachedPhaseAndDoesNotRepeatItsEvent()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = new BlokeRaidService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new SequenceRandom(40, 5, 10, 20),
            clock
        );
        var configured = (
            await service.SaveConfigurationAsync(
                hostId,
                Draft() with
                {
                    MaximumHealth = 100,
                    AttackMinimum = 1,
                    AttackMaximum = 99,
                    AttackCooldownSeconds = 0,
                    PhaseTwoHealthPercent = 65,
                    PhaseThreeHealthPercent = 30,
                    PhaseTwoResponse = "Phase two response.",
                    PhaseThreeResponse = "Phase three response.",
                },
                default
            )
        ).ShouldBeOfType<BlokeRaidConfigurationOutcome.Saved>();
        _ = Success(await service.StartAsync(hostId, Campaign("start"), default));

        var reachedPhaseTwo = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:phase-two", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream"),
                default
            )
        );
        reachedPhaseTwo.Action.PhaseAfter.ShouldBe(2);
        reachedPhaseTwo.Action.Response.ShouldBe("Phase two response.");
        _ = (
            await service.SaveConfigurationAsync(
                hostId,
                Draft() with
                {
                    Revision = configured.Configuration.Revision,
                    MaximumHealth = 100,
                    AttackMinimum = 1,
                    AttackMaximum = 99,
                    AttackCooldownSeconds = 0,
                    PhaseTwoHealthPercent = 50,
                    PhaseThreeHealthPercent = 30,
                    PhaseTwoResponse = "Edited phase two response.",
                    PhaseThreeResponse = "Edited phase three response.",
                },
                default
            )
        ).ShouldBeOfType<BlokeRaidConfigurationOutcome.Saved>();

        var aboveEditedThreshold = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:still-phase-two", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream"),
                default
            )
        );
        var belowEditedThreshold = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:past-phase-two", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream"),
                default
            )
        );
        var reachedPhaseThree = ActionSuccess(
            await service.ActAsync(
                hostId,
                new("chat:phase-three", BlokeRaidActionKind.Attack, Viewer("viewer"), "stream"),
                default
            )
        );

        aboveEditedThreshold.Action.PhaseAfter.ShouldBe(2);
        belowEditedThreshold.Action.PhaseAfter.ShouldBe(2);
        reachedPhaseThree.Action.PhaseAfter.ShouldBe(3);
        reachedPhaseThree.Action.Response.ShouldBe("Edited phase three response.");
        await using var verify = await database.CreateDbContextAsync();
        var phaseEventKeys = await verify
            .BlokeRaidEvents.Where(value => value.Kind == BlokeRaidEventKind.PhaseChanged)
            .OrderBy(value => value.Id)
            .Select(value => value.OperationKey)
            .ToArrayAsync();
        phaseEventKeys.Length.ShouldBe(2);
        phaseEventKeys.Distinct().Count().ShouldBe(phaseEventKeys.Length);
        phaseEventKeys[0].ShouldEndWith(":2");
        phaseEventKeys[1].ShouldEndWith(":3");
    }

    [Test]
    public async Task GuessingRuntime_FeatureOffDoesNotApplyCompletedProviderRound()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = Service(database, clock, 5);
        await SaveConfigurationAsync(service, hostId, Draft() with { CorrectGuessDamage = 7 });
        var started = Success(
            await service.StartAsync(hostId, Campaign("start"), default)
        ).Campaign;
        await SeedCompletedGuessAsync(database, hostId);
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            [],
            clock
        );
        await features.DisableAsync(hostId, HostFeatureFlags.CooperativeGame, default);
        var countsBefore = await CountsAsync(database);
        var runtime = new BlokeRaidRuntime(
            database,
            service,
            NullLogger<BlokeRaidRuntime>.Instance
        );

        await runtime.GuessingChangedAsync(hostId, default);

        (await CountsAsync(database)).ShouldBe(countsBefore);
        await using var verify = await database.CreateDbContextAsync();
        var campaign = await verify.BlokeRaidCampaigns.SingleAsync();
        campaign.PublicId.ShouldBe(started.Id);
        campaign.CurrentHealth.ShouldBe(campaign.MaximumHealth);
        (await verify.BlokeRaidContributions.CountAsync()).ShouldBe(0);
    }

    private static BlokeRaidService Service(
        SqliteBlokeBotDbFactory database,
        TimeProvider clock,
        int outcome
    ) => new(database, TestEventBus.Create<AppEventKind>(), new FixedRandom(outcome), clock);

    private static BlokeRaidConfigurationDraft Draft() =>
        new(
            0,
            "The Null Wyrm",
            25_000,
            1_000,
            168,
            2,
            6,
            20,
            40,
            3,
            7,
            30,
            20,
            8,
            14,
            90,
            5,
            new(75),
            4,
            new(250),
            65,
            30,
            "The boss arrives.",
            "The armour fractures.",
            "The final stand begins.",
            "The boss falls.",
            "The boss escapes.",
            BlokeRaidResetPolicy.Manual,
            DayOfWeek.Monday,
            9
        );

    private static async Task SaveConfigurationAsync(
        BlokeRaidService service,
        int hostId,
        BlokeRaidConfigurationDraft draft
    ) =>
        _ = (
            await service.SaveConfigurationAsync(hostId, draft, default)
        ).ShouldBeOfType<BlokeRaidConfigurationOutcome.Saved>();

    private static BlokeRaidCampaignCommand Campaign(string key) =>
        new(key, new("moderator-id", "moderator"), "test");

    private static BlokeRaidViewer Viewer(string login) => new($"{login}-id", login, login);

    private static BlokeRaidCampaignOutcome.Succeeded Success(BlokeRaidCampaignOutcome outcome) =>
        outcome.ShouldBeOfType<BlokeRaidCampaignOutcome.Succeeded>();

    private static BlokeRaidActionOutcome.Succeeded ActionSuccess(BlokeRaidActionOutcome outcome) =>
        outcome.ShouldBeOfType<BlokeRaidActionOutcome.Succeeded>();

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = HostFeatureFlags.CooperativeGame,
            BlokeRaidAcceptWorkAfterUtc = _now.AddDays(-1).UtcDateTime,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedBalanceAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string login,
        int amount
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = hostId,
                Login = login,
                Amount = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task SeedCompletedGuessAsync(SqliteBlokeBotDbFactory database, int hostId)
    {
        await using var db = await database.CreateDbContextAsync();
        var profile = new GuessRoundProfile
        {
            HostId = hostId,
            Name = "Raid provider round",
            Slug = "raid-provider-round",
            IsDefault = true,
            ReplySettings = new BotReplySettings(),
        };
        _ = db.Rounds.Add(
            new GuessRound
            {
                HostId = hostId,
                GuessRoundProfile = profile,
                Status = GuessRoundStatus.Completed,
                StartedAtUtc = _now.AddMinutes(-5).UtcDateTime,
                ClosedAtUtc = _now.UtcDateTime,
                WinningName = "blue",
                Votes =
                [
                    new GuessVote
                    {
                        Login = "viewer",
                        GuessName = "blue",
                        GuessedAtUtc = _now.AddMinutes(-2).UtcDateTime,
                    },
                ],
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static async Task<BoundaryCounts> CountsAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        return new(
            await db.BlokeRaidCampaigns.CountAsync(),
            await db.BlokeRaidActions.CountAsync(),
            await db.BlokeRaidEvents.CountAsync(),
            await db.PointLedgerEntries.CountAsync()
        );
    }

    private sealed record BoundaryCounts(int Campaigns, int Actions, int Events, int LedgerEntries);

    private sealed class FixedRandom(int outcome) : IBlokeRaidRandom
    {
        public int NextInclusive(int minimum, int maximum)
        {
            outcome.ShouldBeInRange(minimum, maximum);
            return outcome;
        }
    }

    private sealed class SequenceRandom(params int[] outcomes) : IBlokeRaidRandom
    {
        private readonly Queue<int> _outcomes = new(outcomes);

        public int NextInclusive(int minimum, int maximum)
        {
            _outcomes.ShouldNotBeEmpty();
            var outcome = _outcomes.Dequeue();
            outcome.ShouldBeInRange(minimum, maximum);
            return outcome;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }
}
