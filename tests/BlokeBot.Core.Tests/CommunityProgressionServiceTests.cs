using System.Globalization;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
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
    public async Task CompletedAchievement_PresentsSafeHostScopedCardOnceAfterCommit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var hostId = await SeedHostAsync(database, "alpha");
        var otherHostId = await SeedHostAsync(database, "beta");
        await SetFeatureAsync(database, hostId, HostFeatureFlags.All);
        await SetFeatureAsync(database, otherHostId, HostFeatureFlags.All);
        var feed = new OverlayEventFeedService(
            database,
            clock,
            new OverlayPublisherServices(),
            NullLogger<OverlayEventFeedService>.Instance
        );
        var achievementPublisher = new CommunityAchievementOverlayEventPublisher(database, [feed]);
        var service = CreateService(database, clock, achievementObserver: achievementPublisher);
        _ = await SeedEventFeedAsync(database, hostId, "Alpha feed", clock.GetUtcNow());
        var otherOverlayId = await SeedEventFeedAsync(
            database,
            otherHostId,
            "Beta feed",
            clock.GetUtcNow()
        );
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            points: 25,
            withTitle: true,
            definitionKey: "private-definition-key",
            rewardKey: "private-reward-key",
            rewardPresentationToken: "private-reward-token"
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var sourceEvent = new CommunitySourceEvent.ChatMessage(
            "achievement-message",
            new CommunityViewer("private-viewer-id", "viewerlogin", "Viewer Name"),
            _now
        );

        _ = Success(await service.ProcessEventAsync(hostId, sourceEvent, default));
        Success(await service.ProcessEventAsync(hostId, sourceEvent, default))
            .WasIdempotent.ShouldBeTrue();

        await using var db = await database.CreateDbContextAsync();
        var persisted = (await db.OverlayEventFeedItems.ToListAsync()).ShouldHaveSingleItem();
        persisted.HostId.ShouldBe(hostId);
        persisted.OverlayInstanceId.ShouldNotBe(otherOverlayId);
        persisted.Kind.ShouldBe(OverlayEventFeedKind.AchievementCompletion);
        persisted.SourceKey.ShouldBe(
            (await db.CommunityCompletions.SingleAsync()).PublicId.ToString("N")
        );
        var overlay = await db.OverlayInstances.SingleAsync(value => value.HostId == hostId);
        var state = await feed.ReadAsync(
            new ResolvedOverlayInstance(
                hostId,
                overlay.PublicId,
                overlay.Type,
                OverlayConfiguration.EventFeedV1.Default,
                new OverlayRevision(overlay.Revision)
            ),
            default
        );
        var card = state!.Active!;
        card.Body.ShouldContain("Viewer Name");
        card.Body.ShouldContain("Representative achievement");
        card.Body.ShouldContain("25 points");
        card.Body.ShouldContain("Trailblazer");
        card.Body.ShouldNotContain("private-viewer-id");
        card.Body.ShouldNotContain("private moderator material");
        card.Body.ShouldNotContain("private-definition-key");
        card.Body.ShouldNotContain("private-reward-key");
        card.Body.ShouldNotContain("private-reward-token");
    }

    [Test]
    public async Task CommittedSourceEvent_NotifiesTheOwningHostOnceAndIdempotentRetryDoesNotReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var observer = new CommunityProgressionChangeObserver();
        var service = CreateService(database, new ManualTimeProvider(_now), observer);
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ChatMessage,
            CommunityCompletionMode.Repeatable,
            CommunityResetSchedule.None,
            target: 3,
            scope: CommunityProgressScope.Communal
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        observer.HostIds.Clear();
        var sourceEvent = new CommunitySourceEvent.ChatMessage(
            "event-1",
            new CommunityViewer("viewer-1", "viewer", "Viewer"),
            _now
        );

        _ = Success(await service.ProcessEventAsync(hostId, sourceEvent, default));
        Success(await service.ProcessEventAsync(hostId, sourceEvent, default))
            .WasIdempotent.ShouldBeTrue();

        observer.HostIds.ShouldBe([hostId]);
    }

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

        var management = (await service.GetModeratorSeasonsAsync(hostId, default)).Single();
        management.Progress.Single().Amount.ShouldBe(2);
        management.Completions.Single().DefinitionName.ShouldBe("Representative achievement");
        management.Unlocks.Count.ShouldBe(2);

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
        var archived = (await service.GetPublicAsync("alpha", default))
            .ShouldNotBeNull()
            .Seasons.Single();
        archived.Status.ShouldBe(CommunitySeasonStatus.Archived);
        archived.Standings.ShouldBe(closedPublic.Standings);
        archived.Completions.Count.ShouldBe(1);
        archived.Unlocks.Count.ShouldBe(2);
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
        var secondInstance = CreateService(database, clock);
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

        var editOutcomes = await Task.WhenAll(
            service.EditScheduleAsync(hostId, edit, default),
            secondInstance.EditScheduleAsync(hostId, edit, default)
        );
        editOutcomes.Select(Success).Count(value => value.WasIdempotent).ShouldBe(1);

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
        await Task.WhenAll(
            service.RollOverCurrentPeriodsAsync(CommunityRolloverKind.Restart, default),
            secondInstance.RollOverCurrentPeriodsAsync(CommunityRolloverKind.Restart, default)
        );

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
    public async Task ExternalAchievementGrant_UsesInclusiveSeasonWindowWithoutBreakingReplay()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database, new ManualTimeProvider(_now));
        var setup = await ConfigureAsync(
            database,
            service,
            hostId,
            CommunityVisibility.Public,
            CommunityEventRuleKind.ExternalGrant,
            CommunityCompletionMode.OneTime,
            CommunityResetSchedule.None,
            definitionKey: "external-winner"
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var startsAt = _now.AddDays(-1);
        var endsAt = _now.AddDays(60);
        CommunityExternalGrantRequest Request(
            string operation,
            string viewer,
            DateTimeOffset occurredAt
        ) =>
            new(
                hostId,
                "integration",
                operation,
                new("external-winner"),
                new(viewer, viewer, viewer),
                occurredAt
            );

        _ = (
            await service.GrantAsync(Request("before", "before", startsAt.AddTicks(-1)), default)
        ).ShouldBeOfType<CommunityExternalGrantOutcome.AchievementUnavailable>();
        var atStart = Request("at-start", "start", startsAt);
        _ = (
            await service.GrantAsync(atStart, default)
        ).ShouldBeOfType<CommunityExternalGrantOutcome.Granted>();
        (await service.GrantAsync(atStart with { OccurredAtUtc = endsAt.AddDays(1) }, default))
            .ShouldBeOfType<CommunityExternalGrantOutcome.Granted>()
            .WasIdempotent.ShouldBeTrue();
        _ = (
            await service.GrantAsync(
                atStart with
                {
                    Viewer = new("conflict", "conflict", "Conflict"),
                    OccurredAtUtc = endsAt.AddDays(1),
                },
                default
            )
        ).ShouldBeOfType<CommunityExternalGrantOutcome.Conflict>();
        _ = (
            await service.GrantAsync(Request("at-end", "end", endsAt), default)
        ).ShouldBeOfType<CommunityExternalGrantOutcome.Granted>();
        _ = (
            await service.GrantAsync(Request("after", "after", endsAt.AddTicks(1)), default)
        ).ShouldBeOfType<CommunityExternalGrantOutcome.AchievementUnavailable>();

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CommunityExternalGrantReceipts.CountAsync()).ShouldBe(2);
        (await verify.CommunityCompletions.CountAsync()).ShouldBe(2);
    }

    [Test]
    [Arguments(CommunityDefinitionKind.Quest, CommunityProgressScope.Viewer)]
    [Arguments(CommunityDefinitionKind.Achievement, CommunityProgressScope.Communal)]
    public async Task ExternalGrantDefinition_RejectsIncompatibleDefinitionShapes(
        CommunityDefinitionKind kind,
        CommunityProgressScope scope
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database, new ManualTimeProvider(_now));
        _ = Success(
            await service.CreateSeasonAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    "External grant season",
                    "Public",
                    "Private",
                    CommunityVisibility.Public,
                    _now.AddDays(-1).UtcDateTime,
                    _now.AddDays(1).UtcDateTime,
                    Actor()
                ),
                default
            )
        );
        await using var db = await database.CreateDbContextAsync();
        var seasonId = await db.CommunitySeasons.Select(value => value.PublicId).SingleAsync();

        var result = await service.AddDefinitionAsync(
            hostId,
            new(
                Guid.NewGuid(),
                new(seasonId),
                "invalid-external",
                "Invalid external",
                "Must not persist",
                kind,
                scope,
                CommunityCompletionMode.OneTime,
                CommunityEventRuleKind.ExternalGrant,
                CommunityProgressIncrement.Occurrence,
                null,
                1,
                PointAmount.Zero,
                CommunityResetSchedule.None,
                [],
                Actor()
            ),
            default
        );

        _ = result.ShouldBeOfType<CommunityOperationOutcome.Invalid>();
        CommunityEventRuleCatalog
            .AvailableFor(kind, scope)
            .ShouldAllBe(value => value.Kind != CommunityEventRuleKind.ExternalGrant);
        (await db.CommunityDefinitions.CountAsync()).ShouldBe(0);
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

        var publicSeason = (await service.GetPublicAsync("alpha", default))
            .ShouldNotBeNull()
            .Seasons.Single();
        var communal = publicSeason.CommunalProgress.Single();
        communal.Amount.ShouldBe(1);
        communal.Target.ShouldBe(1);
        communal.CompletionCount.ShouldBe(1);
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
        var hiddenManagement = (await service.GetModeratorSeasonsAsync(hostId, default)).Single();
        hiddenManagement.Progress.Single().Amount.ShouldBe(1);
        hiddenManagement.Standings.Single().TwitchUserId.ShouldBe(viewer.TwitchUserId);

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
    public async Task FeatureGateChanges_ReconcileEventSubSubscriptions()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(_now);
        var hostId = await SeedHostAsync(database, "alpha");
        var trigger = new RecordingEventSubReconciliationTrigger();
        var observer = new CommunityProgressionFeatureObserver(
            CreateService(database, clock),
            trigger
        );
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            [observer],
            clock
        );

        await features.DisableAsync(hostId, HostFeatureFlags.CommunityProgression, default);

        trigger.Reconciled.ShouldBeTrue();
        trigger.Reset();

        await features.EnableAsync(hostId, HostFeatureFlags.CommunityProgression, default);

        trigger.Reconciled.ShouldBeTrue();
    }

    [Test]
    public async Task OwnedEntryPoints_WhenDisabled_PreserveStateAndDoNotReplayDelayedSources()
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
            target: 2
        );
        await OpenAsync(service, setup.Season, setup.Revision, hostId);
        var runtime = new CommunityProgressionRuntime(
            database,
            service,
            clock,
            NullLogger<CommunityProgressionRuntime>.Instance
        );
        var bountyObserver = new BountyCommunityProgressionObserver(
            service,
            NullLogger<BountyCommunityProgressionObserver>.Instance
        );
        var commands = new RecordingCommandBuilder();
        new CommunityProgressionCommandModule(database, service).AddCommands(commands);
        var responses = new List<string>();
        var command = CommandContext(
            "viewer",
            "alpha",
            "progress",
            new Dictionary<string, string>
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["user-id"] = "viewer-id",
            },
            response => responses.Add(response.Message)
        );
        var delayedTimestamp = _now.AddMinutes(1);
        var delayedMessage = Message(
            "viewer",
            "alpha",
            "hello",
            new Dictionary<string, string>
            {
                ["id"] = "delayed-chat",
                ["user-id"] = "viewer-id",
                ["tmi-sent-ts"] = delayedTimestamp
                    .ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture),
            }
        );
        var features = new HostFeatureService(
            database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [],
            [],
            clock
        );
        await features.DisableAsync(hostId, HostFeatureFlags.CommunityProgression, default);

        await runtime.MessageReceivedAsync(delayedMessage, default);
        await bountyObserver.BountyCompletedAsync(
            hostId,
            Guid.NewGuid(),
            delayedTimestamp,
            default
        );
        await commands[FixedChatCommandRoutes.Progress](command, [], default);

        responses.ShouldBeEmpty();
        clock.Advance(TimeSpan.FromMinutes(2));
        await features.EnableAsync(hostId, HostFeatureFlags.CommunityProgression, default);
        await runtime.MessageReceivedAsync(delayedMessage, default);
        await bountyObserver.BountyCompletedAsync(
            hostId,
            Guid.NewGuid(),
            delayedTimestamp,
            default
        );
        await runtime.MessageReceivedAsync(
            Message(
                "viewer",
                "alpha",
                "current",
                new Dictionary<string, string>
                {
                    ["id"] = "current-chat",
                    ["user-id"] = "viewer-id",
                    ["tmi-sent-ts"] = clock
                        .GetUtcNow()
                        .ToUnixTimeMilliseconds()
                        .ToString(CultureInfo.InvariantCulture),
                }
            ),
            default
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CommunityProgress.SingleAsync()).Amount.ShouldBe(1);
        var receipts = await verify
            .CommunitySourceEventReceipts.Select(value => value.SourceEventId)
            .ToListAsync();
        receipts.ShouldBe(["current-chat"]);
    }

    [Test]
    public async Task TwitchObserver_UsesBroadcasterIdentityWithoutCrossHostLoginFallback()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHost = await SeedHostAsync(database, "first");
        var secondHost = await SeedHostAsync(database, "second");
        var clock = new ManualTimeProvider(_now);
        var service = CreateService(database, clock);
        foreach (var hostId in new[] { firstHost, secondHost })
        {
            var setup = await ConfigureAsync(
                database,
                service,
                hostId,
                CommunityVisibility.Public,
                CommunityEventRuleKind.Follow,
                CommunityCompletionMode.OneTime,
                CommunityResetSchedule.None,
                target: 2
            );
            await OpenAsync(service, setup.Season, setup.Revision, hostId);
        }
        var runtime = new CommunityProgressionRuntime(
            database,
            service,
            clock,
            NullLogger<CommunityProgressionRuntime>.Instance
        );

        await runtime.FollowReceivedAsync(
            new(
                "follow-authority",
                _now,
                "viewer-id",
                "viewer",
                "Viewer",
                "first-id",
                "second",
                "Second",
                _now
            ),
            default
        );

        await using var verify = await database.CreateDbContextAsync();
        var progress = await verify.CommunityProgress.SingleAsync();
        progress.HostId.ShouldBe(firstHost);
        progress.ViewerTwitchUserId.ShouldBe("viewer-id");
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
        TimeProvider clock,
        ICommunityProgressionChangeObserver? observer = null,
        ICommunityAchievementCompletionObserver? achievementObserver = null
    ) =>
        new(
            database,
            TestEventBus.Create<AppEventKind>(),
            clock,
            observer is null ? null : [observer],
            achievementObserver is null ? null : [achievementObserver]
        );

    private static async Task<long> SeedEventFeedAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string name,
        DateTimeOffset now
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            Name = name,
            Type = OverlayType.EventFeed,
            IsEnabled = true,
            ConfigurationJson = OverlayConfiguration.EventFeedV1.Default.ToPersistenceJson(),
            AccessKeyDigest = Enumerable.Repeat(checked((byte)hostId), 32).ToArray(),
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime,
        };
        _ = db.OverlayInstances.Add(overlay);
        _ = await db.SaveChangesAsync();
        return overlay.Id;
    }

    private sealed class OverlayPublisherServices : IServiceProvider
    {
        private readonly IOverlayLivePublisher _publisher = new NoopOverlayPublisher();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IOverlayLivePublisher) ? _publisher : null;
    }

    private sealed class NoopOverlayPublisher : IOverlayLivePublisher
    {
        public void PublishState(ResolvedOverlayInstance instance) { }

        public void PublishTest(ResolvedOverlayInstance instance) { }
    }

    private sealed class CommunityProgressionChangeObserver : ICommunityProgressionChangeObserver
    {
        internal List<int> HostIds { get; } = [];

        public ValueTask CommunityProgressionChangedAsync(
            int hostId,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            HostIds.Add(hostId);
            return ValueTask.CompletedTask;
        }
    }

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
        CommunityProgressScope scope = CommunityProgressScope.Viewer,
        string rewardKey = "trailblazer",
        string rewardPresentationToken = "trailblazer"
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
                        rewardKey,
                        CommunityRewardKind.Title,
                        "Trailblazer",
                        rewardPresentationToken,
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

    private static ChatMessage Message(
        string login,
        string channel,
        string text,
        IReadOnlyDictionary<string, string> tags
    ) => new(login, channel, text, text, tags);

    private static ChatCommandContext CommandContext(
        string login,
        string channel,
        string commandName,
        IReadOnlyDictionary<string, string> tags,
        Action<CommandResponse> respond
    ) =>
        new()
        {
            Message = Message(login, channel, $"!{commandName}", tags),
            CommandName = commandName,
            Responder = (response, _) =>
            {
                respond(response);
                return ValueTask.CompletedTask;
            },
        };

    private sealed class RecordingCommandBuilder : IChatCommandBuilder
    {
        private readonly Dictionary<string, ChatCommandHandler> _handlers = new(
            StringComparer.Ordinal
        );

        public ChatCommandHandler this[FixedChatCommandRoute route] => _handlers[route.Value];

        public IChatCommandBuilder Map(string route, ChatCommandHandler handler)
        {
            _handlers.Add(route, handler);
            return this;
        }

        public IChatCommandBuilder Map(FixedChatCommandRoute route, ChatCommandHandler handler) =>
            Map(route.Value, handler);

        public IChatCommandBuilder MapDynamic(DynamicChatCommandHandler handler) => this;

        public IChatCommandBuilder MapFallback(ChatCommandHandler handler) => this;

        public IChatCommandBuilder UseFilter<TFilter>()
            where TFilter : class, IChatCommandFilter => this;
    }

    private sealed class RecordingEventSubReconciliationTrigger
        : IEventSubChannelReconciliationTrigger
    {
        internal bool Reconciled { get; private set; }

        public Task ReconcileAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reconciled = true;
            return Task.CompletedTask;
        }

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Revocation reconciliation was not expected.");

        internal void Reset() => Reconciled = false;
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
