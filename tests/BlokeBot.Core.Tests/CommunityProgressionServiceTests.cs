using System.Globalization;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CommunityProgressionServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SupportedEvents_CompleteAtomicallyExactlyOnceAndFreezeClosedStandings()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database, clock);
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            target: 2,
            points: 25,
            withTitle: true
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var viewer = new CommunityViewer("viewer-1", "Viewer", "Viewer One");
        var first = new CommunitySourceEvent.ChatMessage("event-1", viewer, _now);
        var second = new CommunitySourceEvent.ChatMessage("event-2", viewer, _now.AddMinutes(1));

        _ = Success(await service.ProcessEventAsync(hostId, first, default));
        var duplicate = Success(await service.ProcessEventAsync(hostId, first, default));
        _ = Success(await service.ProcessEventAsync(hostId, second, default));

        duplicate.WasIdempotent.ShouldBeTrue();
        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.CommunitySourceEventReceipts.CountAsync()).ShouldBe(2);
            (await verify.CommunityCompletions.CountAsync()).ShouldBe(1);
            (await verify.CommunityRewardUnlocks.CountAsync()).ShouldBe(1);
            (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("25");
            var ledger = await verify.PointLedgerEntries.SingleAsync(value =>
                value.Kind == PointLedgerKind.CommunityProgressionReward
            );
            _ = ledger.CommunityCompletionId.ShouldNotBeNull();
        }

        await using (var seed = await database.CreateDbContextAsync())
        {
            var completion = await seed.CommunityCompletions.SingleAsync();
            var secondTitle = new CommunityRewardDefinition
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                SeasonId = completion.SeasonId,
                Key = "veteran",
                Kind = CommunityRewardKind.Title,
                Name = "Veteran",
                PresentationToken = "veteran",
                CreatedAtUtc = _now.UtcDateTime,
            };
            _ = seed.CommunityRewardDefinitions.Add(secondTitle);
            _ = await seed.SaveChangesAsync();
            _ = seed.CommunityRewardUnlocks.Add(
                new()
                {
                    HostId = hostId,
                    RewardDefinitionId = secondTitle.Id,
                    ViewerTwitchUserId = viewer.TwitchUserId,
                    ViewerLogin = viewer.Login,
                    ViewerDisplayName = viewer.DisplayName,
                    CompletionId = completion.Id,
                    GrantedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var equip = new CommunityEquipCommand(
            Guid.NewGuid(),
            hostId,
            viewer,
            CommunityRewardKind.Title,
            "trailblazer"
        );
        _ = Success(await service.EquipAsync(equip, default));
        Success(await service.EquipAsync(equip, default)).WasIdempotent.ShouldBeTrue();
        _ = (
            await service.EquipAsync(equip with { RewardKey = "veteran" }, default)
        ).ShouldBeOfType<CommunityOperationOutcome.Conflict>();
        _ = Success(
            await service.EquipAsync(
                equip with
                {
                    OperationId = Guid.NewGuid(),
                    RewardKey = "veteran",
                },
                default
            )
        );
        Success(await service.EquipAsync(equip, default)).WasIdempotent.ShouldBeTrue();
        await using (var verify = await database.CreateDbContextAsync())
        {
            var equippedRewardId = await verify
                .CommunityEquippedRewards.Where(value =>
                    value.HostId == hostId && value.ViewerTwitchUserId == viewer.TwitchUserId
                )
                .Select(value => value.RewardDefinitionId)
                .SingleAsync();
            (
                await verify.CommunityRewardDefinitions.SingleAsync(value =>
                    value.Id == equippedRewardId
                )
            ).Key.ShouldBe("veteran");
        }

        var season = (await service.GetModeratorSeasonsAsync(hostId, default)).Single();
        _ = Success(
            await service.TransitionSeasonAsync(
                hostId,
                Transition(season, CommunitySeasonTransition.Close),
                default
            )
        );
        var closedPublic = (await service.GetPublicAsync("alpha", default))
            .ShouldNotBeNull()
            .Seasons.Single();
        closedPublic.Standings.Single().CompletedCount.ShouldBe(1);

        _ = Success(
            await service.ProcessEventAsync(
                hostId,
                new CommunitySourceEvent.ChatMessage("event-after-close", viewer, _now.AddHours(1)),
                default
            )
        );
        var afterClose = (await service.GetPublicAsync("alpha", default))
            .ShouldNotBeNull()
            .Seasons.Single();
        afterClose.Standings.ShouldBe(closedPublic.Standings);
        afterClose.Completions.Count.ShouldBe(1);

        season = (await service.GetModeratorSeasonsAsync(hostId, default)).Single();
        _ = Success(
            await service.TransitionSeasonAsync(
                hostId,
                Transition(season, CommunitySeasonTransition.Archive),
                default
            )
        );
        (await service.GetViewerUnlocksAsync(hostId, viewer.TwitchUserId, default))
            .Single(value => value.Name == "Veteran")
            .Equipped.ShouldBeTrue();
    }

    [Test]
    public async Task ActiveScheduleEditAndRestart_RollCurrentPeriodOnceWithoutDowntimeReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var hostId = await SeedHostAsync(database, "alpha", "Europe/London");
        var service = CreateService(database, clock);
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.Repeatable,
            new(CommunityResetCadence.Daily, new TimeOnly(6, 0), null),
            target: 5
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var viewer = new CommunityViewer("viewer-1", "viewer", "Viewer");
        _ = Success(
            await service.ProcessEventAsync(
                hostId,
                new CommunitySourceEvent.ChatMessage("event-1", viewer, _now),
                default
            )
        );
        var definition = (await service.GetModeratorSeasonsAsync(hostId, default))
            .Single()
            .Definitions.Single();
        var operationId = Guid.NewGuid();
        var edit = new CommunityScheduleEditCommand(
            operationId,
            definition.Id,
            new(CommunityResetCadence.Weekly, new TimeOnly(8, 30), DayOfWeek.Sunday),
            true,
            Actor(),
            "confirmed"
        );

        _ = Success(await service.EditScheduleAsync(hostId, edit, default));
        Success(await service.EditScheduleAsync(hostId, edit, default))
            .WasIdempotent.ShouldBeTrue();

        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.CommunityProgress.SingleAsync()).Amount.ShouldBe(0);
            (
                await verify.CommunityAudits.CountAsync(value => value.Action == "ScheduleEdited")
            ).ShouldBe(1);
            (
                await verify.CommunityResetPeriods.CountAsync(value =>
                    value.RolloverKind == CommunityRolloverKind.ScheduleEdit
                )
            ).ShouldBe(1);
        }

        clock.Advance(TimeSpan.FromDays(22));
        await service.RollOverCurrentPeriodsAsync(CommunityRolloverKind.Restart, default);
        await service.RollOverCurrentPeriodsAsync(CommunityRolloverKind.Restart, default);

        await using var final = await database.CreateDbContextAsync();
        var periods = await final
            .CommunityResetPeriods.Where(value => value.DefinitionId == setup.DefinitionId)
            .OrderBy(value => value.StartedAtUtc)
            .ToListAsync();
        periods.Count(value => value.ClosedAtUtc is null).ShouldBe(1);
        periods.Count(value => value.RolloverKind == CommunityRolloverKind.Restart).ShouldBe(2);
    }

    [Test]
    public void ResetResolver_UsesFirstOverlapOccurrenceAndMovesGapForward()
    {
        const string Zone = "Europe/London";

        var gap = CommunityResetScheduleResolver.Resolve(
            Zone,
            new(CommunityResetCadence.Daily, new TimeOnly(1, 30), null),
            1,
            new DateTimeOffset(2026, 3, 29, 4, 0, 0, TimeSpan.Zero)
        );
        var overlap = CommunityResetScheduleResolver.Resolve(
            Zone,
            new(CommunityResetCadence.Daily, new TimeOnly(1, 30), null),
            1,
            new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.Zero)
        );

        gap.StartedAtUtc.ShouldBe(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero));
        overlap.StartedAtUtc.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task ExternalAchievementGrant_IsHostScopedAtomicAndIdempotencyKeyed()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "first");
        var secondHost = await SeedHostAsync(database, "second");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var first = await ConfigureAsync(
            database,
            service,
            firstHost,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ExternalGrant,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            withTitle: true,
            definitionKey: "bingo-winner"
        );
        await OpenAsync(service, first.Season, first.Revision, firstHost);
        var second = await ConfigureAsync(
            database,
            service,
            secondHost,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            definitionKey: "bingo-winner"
        );
        await OpenAsync(service, second.Season, second.Revision, secondHost);
        var request = new CommunityExternalGrantRequest(
            firstHost,
            "bingo",
            "game-42:winner",
            new("bingo-winner"),
            new("winner-id", "winner", "Winner"),
            _now
        );

        var granted = await service.GrantAsync(request, default);
        var retried = await service.GrantAsync(request, default);
        var conflict = await service.GrantAsync(
            request with
            {
                Viewer = new("other-id", "other", "Other"),
            },
            default
        );
        var otherHost = await service.GrantAsync(request with { HostId = secondHost }, default);

        granted
            .ShouldBeOfType<CommunityExternalGrantOutcome.Granted>()
            .WasIdempotent.ShouldBeFalse();
        retried
            .ShouldBeOfType<CommunityExternalGrantOutcome.Granted>()
            .WasIdempotent.ShouldBeTrue();
        _ = conflict.ShouldBeOfType<CommunityExternalGrantOutcome.Conflict>();
        _ = otherHost.ShouldBeOfType<CommunityExternalGrantOutcome.AchievementUnavailable>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CommunityCompletions.CountAsync()).ShouldBe(1);
        (await verify.CommunityRewardUnlocks.CountAsync()).ShouldBe(1);
        (await verify.CommunityExternalGrantReceipts.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task CompletedBountyReconciliation_RepairsMissedObserverDeliveryExactlyOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database, new ManualTimeProvider(_now));
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.BountyCompleted,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            scope: CommunityProgressScope.Communal
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var bountyPublicId = Guid.NewGuid();
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.Bounties.Add(
                new()
                {
                    PublicId = bountyPublicId,
                    HostId = hostId,
                    CreationOperationId = Guid.NewGuid(),
                    CreationFingerprint = "reconciliation-test",
                    Title = "Completed community bounty",
                    Description = "Durable source record",
                    Status = BountyStatus.Completed,
                    Visibility = BountyVisibility.Public,
                    FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
                    RewardDistribution = BountyRewardDistribution.Proportional,
                    Revision = 1,
                    ExpiresAtUtc = _now.AddHours(1).UtcDateTime,
                    CreatedAtUtc = _now.AddHours(-1).UtcDateTime,
                    UpdatedAtUtc = _now.UtcDateTime,
                    ResolvedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var openSeason = (await service.GetModeratorSeasonsAsync(hostId, default)).Single();
        _ = Success(
            await service.TransitionSeasonAsync(
                hostId,
                Transition(openSeason, CommunitySeasonTransition.Close),
                default
            )
        );
        await service.ReconcileCompletedBountyEventsAsync(default);

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CommunitySourceEventReceipts.CountAsync()).ShouldBe(1);
        (await verify.CommunityCompletions.CountAsync()).ShouldBe(1);
        (await verify.CommunitySeasons.SingleAsync()).Status.ShouldBe(CommunitySeasonStatus.Closed);
    }

    [Test]
    public async Task DisabledAndHiddenStates_BlockMutationPublicOutputAndPreserveRetainedProgress()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Hidden,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            target: 3
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var viewer = new CommunityViewer("viewer-id", "viewer", "Viewer");
        _ = Success(
            await service.ProcessEventAsync(
                hostId,
                new CommunitySourceEvent.ChatMessage("before-disable", viewer, _now),
                default
            )
        );
        (await service.GetPublicAsync("alpha", default)).ShouldBeNull();

        var featureEvents = TestEventBus.Create<AppEventKind>();
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(featureEvents),
            [],
            [],
            clock
        );
        await features.DisableAsync(hostId, HostFeatureFlags.CommunityProgression, default);
        clock.Advance(TimeSpan.FromMinutes(2));
        var suppressed = new CommunitySourceEvent.ChatMessage(
            "suppressed",
            viewer,
            _now.AddMinutes(1)
        );
        var blocked = await service.ProcessEventAsync(hostId, suppressed, default);

        _ = blocked.ShouldBeOfType<CommunityOperationOutcome.FeatureDisabled>();
        (await service.GetPublicAsync("alpha", default)).ShouldBeNull();
        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.CommunityProgress.SingleAsync()).Amount.ShouldBe(1);
            (
                await verify.CommunitySourceEventReceipts.AnyAsync(value =>
                    value.SourceEventId == "suppressed"
                )
            ).ShouldBeFalse();
        }

        await features.EnableAsync(hostId, HostFeatureFlags.CommunityProgression, default);
        Success(await service.ProcessEventAsync(hostId, suppressed, default))
            .WasIdempotent.ShouldBeTrue();
        var retained = (await service.GetModeratorSeasonsAsync(hostId, default)).Single();
        retained.Status.ShouldBe(CommunitySeasonStatus.Open);
        await using var final = await database.CreateDbContextAsync();
        (await final.CommunityProgress.SingleAsync()).Amount.ShouldBe(1);
    }

    [Test]
    public async Task SameProviderEventIdentity_AdvancesEachHostIndependently()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "first");
        var secondHost = await SeedHostAsync(database, "second");
        var service = CreateService(database, new ManualTimeProvider(_now));
        foreach (var hostId in new[] { firstHost, secondHost })
        {
            var setup = await ConfigureAsync(
                database,
                service,
                hostId,
                CommunityVisibility.Public,
                CommunityEventRuleKind.ChatMessage,
                CommunityCompletionMode.OneTime,
                CommunityResetSchedule.None,
                target: 2
            );
            await OpenAsync(service, setup.Season, setup.Revision, hostId);
        }
        var source = new CommunitySourceEvent.ChatMessage(
            "shared-provider-id",
            new("viewer-id", "viewer", "Viewer"),
            _now
        );

        _ = Success(await service.ProcessEventAsync(firstHost, source, default));
        _ = Success(await service.ProcessEventAsync(secondHost, source, default));

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CommunitySourceEventReceipts.CountAsync()).ShouldBe(2);
        (await verify.CommunityProgress.CountAsync()).ShouldBe(2);
        (
            await verify.CommunityProgress.AllAsync(value =>
                value.Amount == 1 && value.ViewerTwitchUserId == "viewer-id"
            )
        ).ShouldBeTrue();
    }

    [Test]
    public async Task EventSubRequirementSource_FollowsOpenRulesAndFeatureGate()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database, clock);
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.Follow,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var runtime = new CommunityProgressionRuntime(
            database,
            service,
            clock,
            NullLogger<CommunityProgressionRuntime>.Instance
        );

        (
            await runtime.RequiresAsync("alpha", AutomationEventSubRequirement.Follows, default)
        ).ShouldBeTrue();
        (
            await runtime.RequiresAsync("alpha", AutomationEventSubRequirement.Cheers, default)
        ).ShouldBeFalse();
        await SetFeatureAsync(database, hostId, HostFeatureFlags.None);
        (
            await runtime.RequiresAsync("alpha", AutomationEventSubRequirement.Follows, default)
        ).ShouldBeFalse();
    }

    [Test]
    public async Task PointRewardFailure_RollsBackReceiptProgressCompletionAndUnlock()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database, new ManualTimeProvider(_now));
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            points: 1,
            withTitle: true
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "viewer",
                    Amount = PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture),
                    UpdatedAtUtc = _now.UtcDateTime,
                }
            );
            _ = await seed.SaveChangesAsync();
        }

        var result = await service.ProcessEventAsync(
            hostId,
            new CommunitySourceEvent.ChatMessage(
                "atomic-failure",
                new("viewer-id", "viewer", "Viewer"),
                _now
            ),
            default
        );

        _ = result.ShouldBeOfType<CommunityOperationOutcome.Conflict>();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CommunitySourceEventReceipts.CountAsync()).ShouldBe(0);
        (await verify.CommunityProgress.CountAsync()).ShouldBe(0);
        (await verify.CommunityCompletions.CountAsync()).ShouldBe(0);
        (await verify.CommunityRewardUnlocks.CountAsync()).ShouldBe(0);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    private static CommunityProgressionService CreateService(
        SqliteBlokeBotDbFactory database,
        TimeProvider clock
    ) => new(database, TestEventBus.Create<AppEventKind>(), clock);

    private static async Task<ConfiguredSeason> ConfigureAsync(
        SqliteBlokeBotDbFactory database,
        CommunityProgressionService service,
        int hostId,
        CommunityVisibility visibility,
        CommunityEventRuleKind eventRule,
        CommunityCompletionMode completionMode,
        CommunityResetSchedule schedule,
        long target = 1,
        int points = 0,
        bool withTitle = false,
        string definitionKey = "representative",
        CommunityProgressScope scope = CommunityProgressScope.Viewer
    )
    {
        _ = Success(
            await service.CreateSeasonAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    $"Season {hostId}",
                    "Public season description",
                    "private moderator material",
                    visibility,
                    _now.AddDays(-1).UtcDateTime,
                    _now.AddDays(60).UtcDateTime,
                    Actor()
                ),
                default
            )
        );
        await using var db = await database.CreateDbContextAsync();
        var season = await db.CommunitySeasons.SingleAsync(value => value.HostId == hostId);
        var rewardIds = new List<CommunityRewardId>();
        if (withTitle)
        {
            _ = Success(
                await service.AddRewardAsync(
                    hostId,
                    new(
                        Guid.NewGuid(),
                        new(season.PublicId),
                        "trailblazer",
                        CommunityRewardKind.Title,
                        "Trailblazer",
                        "trailblazer",
                        Actor()
                    ),
                    default
                )
            );
            var rewardId = await db
                .CommunityRewardDefinitions.Where(value => value.HostId == hostId)
                .Select(value => value.PublicId)
                .SingleAsync();
            rewardIds.Add(new(rewardId));
        }
        _ = Success(
            await service.AddDefinitionAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    new(season.PublicId),
                    definitionKey,
                    "Representative achievement",
                    "Stable supported behavior",
                    completionMode == CommunityCompletionMode.OneTime
                        ? CommunityDefinitionKind.Achievement
                        : CommunityDefinitionKind.Quest,
                    scope,
                    completionMode,
                    eventRule,
                    CommunityProgressIncrement.Occurrence,
                    null,
                    target,
                    new PointAmount(points),
                    schedule,
                    rewardIds,
                    Actor()
                ),
                default
            )
        );
        var definitionId = await db
            .CommunityDefinitions.Where(value => value.HostId == hostId)
            .Select(value => value.Id)
            .SingleAsync();
        return new(new(season.PublicId), season.Revision, definitionId);
    }

    private static async Task OpenAsync(
        CommunityProgressionService service,
        CommunitySeasonId season,
        long revision,
        int hostId
    ) =>
        _ = Success(
            await service.TransitionSeasonAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    season,
                    revision,
                    CommunitySeasonTransition.Open,
                    Actor(),
                    "open"
                ),
                default
            )
        );

    private static CommunitySeasonTransitionCommand Transition(
        CommunitySeasonView season,
        CommunitySeasonTransition transition
    ) =>
        new(Guid.NewGuid(), season.Id, season.Revision, transition, Actor(), transition.ToString());

    private static CommunityActor Actor() => new("host-id", "host");

    private static CommunityOperationOutcome.Succeeded Success(CommunityOperationOutcome result) =>
        result.ShouldBeOfType<CommunityOperationOutcome.Succeeded>();

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        string timeZoneId = "UTC"
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            TimeZoneId = timeZoneId,
            EnabledFeatures = HostFeatureFlags.CommunityProgression,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SetFeatureAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        _ = await db.SaveChangesAsync();
    }

    private sealed record ConfiguredSeason(
        CommunitySeasonId Season,
        long Revision,
        long DefinitionId
    );

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
