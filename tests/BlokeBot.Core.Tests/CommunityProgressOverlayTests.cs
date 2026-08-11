using System.Text.Json;
using System.Text.Json.Nodes;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CommunityProgressOverlayTests
{
    [Test]
    public void Configurations_RoundTripStrictTypedSelectionRotationCalloutsAndAppearance()
    {
        var selected = Guid.Parse("74d8e174-c23f-4c74-a7b4-91608c4348aa");
        var appearance = new OverlayAppearance(100, 80, 680, 340, ".accent{opacity:.9;}");
        OverlayConfiguration.ProgressOverlayV1[] configurations =
        [
            new OverlayConfiguration.CommunityGoalV1(selected, 15, 0, appearance),
            new OverlayConfiguration.ViewerFundedBountyV1(null, 120, 5, appearance),
        ];

        foreach (var configuration in configurations)
        {
            var persisted = configuration.ToPersistenceJson();
            var parsed = OverlayConfiguration
                .Parse(configuration.Type, persisted)
                .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
                .Value.ShouldBeAssignableTo<OverlayConfiguration.ProgressOverlayV1>();

            parsed.ShouldBe(configuration);
            foreach (
                var invalid in new[]
                {
                    ReplaceNumber(persisted, "rotationSeconds", 4),
                    ReplaceNumber(persisted, "rotationSeconds", 121),
                    ReplaceNumber(persisted, "recentContributorCount", 6),
                    WithNull(persisted, "appearance"),
                    persisted[..^1] + ",\"extra\":true}",
                }
            )
            {
                _ = OverlayConfiguration
                    .Parse(configuration.Type, invalid)
                    .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
            }
        }
    }

    [Test]
    public async Task Projection_IsHostScopedPublicAndParentGatedWithRetainedCurrentState()
    {
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedProjectionAsync(database, now.UtcDateTime);
        var clock = new FixedTimeProvider(now);
        var provider = new OverlayStateProvider(database, new OverlayServerEpoch(), clock);
        var goal = Instance(
            seed.HostId,
            OverlayType.CommunityGoal,
            new OverlayConfiguration.CommunityGoalV1(null, 20, 0)
        );
        var bounty = Instance(
            seed.HostId,
            OverlayType.ViewerFundedBounty,
            new OverlayConfiguration.ViewerFundedBountyV1(null, 20, 2)
        );

        var goalSnapshot = (await provider.ProjectAsync(goal, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.CommunityGoalV1>()
            .Snapshot;
        var goalItem = goalSnapshot.State.Items.ShouldHaveSingleItem();
        goalItem.Id.ShouldBe(seed.PublicGoalId);
        goalItem.Title.ShouldBe("Community bounty drive");
        goalItem.Current.ShouldBe("3");
        goalItem.Target.ShouldBe("4");
        goalItem.Percentage.ShouldBe(75);
        goalItem.State.ShouldBe(ProgressOverlayItemState.Active);
        goalItem.RecentContributors.ShouldBeEmpty();

        var bountySnapshot = (await provider.ProjectAsync(bounty, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.ViewerFundedBountyV1>()
            .Snapshot;
        var bountyItem = bountySnapshot.State.Items.ShouldHaveSingleItem();
        bountyItem.Id.ShouldBe(seed.PublicBountyId);
        bountyItem.State.ShouldBe(ProgressOverlayItemState.Active);
        bountyItem.RecentContributors.ShouldBe([new("latest", "300"), new("earlier", "600")]);
        var publicJson = JsonSerializer.Serialize(bountySnapshot, _jsonOptions);
        publicJson.ShouldNotContain("private-bounty");
        publicJson.ShouldNotContain("hidden-goal");
        publicJson.ShouldNotContain("viewer-goal");
        publicJson.ShouldNotContain("latest-user-id");
        publicJson.ShouldNotContain("moderator-secret");
        publicJson.ShouldNotContain("balance");

        foreach (
            var transition in new[]
            {
                (BountyStatus.Accepted, ProgressOverlayItemState.Accepted),
                (BountyStatus.Completed, ProgressOverlayItemState.Completed),
                (BountyStatus.Failed, ProgressOverlayItemState.Failed),
                (BountyStatus.Expired, ProgressOverlayItemState.Expired),
            }
        )
        {
            await SetBountyStatusAsync(database, seed.PublicBountyId, transition.Item1);
            (await provider.ProjectAsync(bounty, CancellationToken.None))
                .ShouldBeOfType<OverlaySnapshotProjection.ViewerFundedBountyV1>()
                .Snapshot.State.Items.ShouldHaveSingleItem()
                .State.ShouldBe(transition.Item2);
        }

        await SetFeaturesAsync(
            database,
            seed.HostId,
            HostFeatureFlags.Overlays | HostFeatureFlags.Bounties
        );
        _ = (
            await provider.ProjectAsync(goal, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
        _ = (
            await provider.ProjectProgressSampleAsync(
                goal,
                ProgressOverlaySampleState.Completed,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
        var retainedGoalCount = await CountGoalsAsync(database, seed.HostId);

        await SetFeaturesAsync(
            database,
            seed.HostId,
            HostFeatureFlags.Overlays
                | HostFeatureFlags.Bounties
                | HostFeatureFlags.CommunityProgression
        );
        (await provider.ProjectAsync(goal, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.CommunityGoalV1>()
            .Snapshot.Animation.ShouldBe("none");
        (await CountGoalsAsync(database, seed.HostId)).ShouldBe(retainedGoalCount);

        var otherHostGoal = Instance(
            seed.OtherHostId,
            OverlayType.CommunityGoal,
            new OverlayConfiguration.CommunityGoalV1(seed.PublicGoalId, 20, 0)
        );
        (await provider.ProjectAsync(otherHostGoal, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.CommunityGoalV1>()
            .Snapshot.State.Items.ShouldBeEmpty();
    }

    [Test]
    public async Task LiveTransitions_AreHostScopedCoalescedAndReconnectWithoutReplay()
    {
        var provider = new MutableProgressProvider();
        var first = Instance(
            1,
            OverlayType.CommunityGoal,
            new OverlayConfiguration.CommunityGoalV1(null, 20, 0)
        );
        var otherHost = first with { HostId = 2, OverlayId = Guid.NewGuid() };
        var bounty = Instance(
            1,
            OverlayType.ViewerFundedBounty,
            new OverlayConfiguration.ViewerFundedBountyV1(null, 20, 3)
        );
        provider.Set(first, "30", ProgressOverlayItemState.Active);
        provider.Set(otherHost, "40", ProgressOverlayItemState.Active);
        provider.Set(bounty, "50", ProgressOverlayItemState.Active);
        await using var coordinator = new OverlayLiveCoordinator(
            new OverlayServerEpoch(),
            provider,
            TimeProvider.System,
            TestEventBus.Create<AppEventKind>(),
            NullLogger<OverlayLiveCoordinator>.Instance
        );
        await coordinator.StartAsync(CancellationToken.None);
        var firstConnection = await OpenAsync(coordinator, first);
        var otherConnection = await OpenAsync(coordinator, otherHost);
        var bountyConnection = await OpenAsync(coordinator, bounty);
        (await ReadLiveAsync(firstConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.CommunityGoalBaseline>()
            .Envelope.Payload.Animation.ShouldBe("none");
        _ = await ReadLiveAsync(otherConnection);
        _ = await ReadLiveAsync(bountyConnection);

        provider.Set(first, "60", ProgressOverlayItemState.Active);
        await coordinator.CommunityProgressionChangedAsync(1, CancellationToken.None);
        (await ReadLiveAsync(firstConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.CommunityGoalEvent>()
            .Envelope.Payload.Animation.ShouldBe("progress");
        otherConnection.Messages.TryRead(out _).ShouldBeFalse();
        bountyConnection.Messages.TryRead(out _).ShouldBeFalse();

        provider.Set(first, "0", ProgressOverlayItemState.Active, completionCount: 1);
        await coordinator.CommunityProgressionChangedAsync(1, CancellationToken.None);
        (await ReadLiveAsync(firstConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.CommunityGoalEvent>()
            .Envelope.Payload.Animation.ShouldBe("complete");

        var reconnect = await OpenAsync(coordinator, first);
        var baseline = (
            await ReadLiveAsync(reconnect)
        ).ShouldBeOfType<OverlayLiveTransportMessage.CommunityGoalBaseline>();
        baseline.Envelope.Payload.Animation.ShouldBe("none");
        baseline.Envelope.Payload.State.Items.ShouldHaveSingleItem().Current.ShouldBe("0");
        await coordinator.StopAsync(CancellationToken.None);
    }

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static ResolvedOverlayInstance Instance(
        int hostId,
        OverlayType type,
        OverlayConfiguration configuration
    ) => new(hostId, Guid.NewGuid(), type, configuration, new OverlayRevision(1));

    private static async Task<ProjectionSeed> SeedProjectionAsync(
        SqliteBlokeBotDbFactory database,
        DateTime now
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = Host("alpha", HostFeatureFlags.All);
        var otherHost = Host("beta", HostFeatureFlags.All);
        db.Hosts.AddRange(host, otherHost);
        _ = await db.SaveChangesAsync();

        var publicSeason = Season(host.Id, "Public season", CommunityVisibility.Public, now);
        var hiddenSeason = Season(host.Id, "moderator-secret", CommunityVisibility.Hidden, now);
        var publicGoal = Definition(
            host.Id,
            publicSeason,
            "Community bounty drive",
            CommunityProgressScope.Communal,
            Guid.Parse("97cb36aa-cfa1-4203-9f23-09dd0d796a01")
        );
        var viewerGoal = Definition(
            host.Id,
            publicSeason,
            "viewer-goal",
            CommunityProgressScope.Viewer,
            Guid.NewGuid()
        );
        var hiddenGoal = Definition(
            host.Id,
            hiddenSeason,
            "hidden-goal",
            CommunityProgressScope.Communal,
            Guid.NewGuid()
        );
        db.CommunitySeasons.AddRange(publicSeason, hiddenSeason);
        db.CommunityDefinitions.AddRange(publicGoal, viewerGoal, hiddenGoal);
        _ = await db.SaveChangesAsync();
        _ = db.CommunityProgress.Add(
            new CommunityProgress
            {
                HostId = host.Id,
                SeasonId = publicSeason.Id,
                DefinitionId = publicGoal.Id,
                SubjectKey = "communal",
                Amount = 3,
                CompletionCount = 0,
                UpdatedAtUtc = now,
            }
        );

        var publicBounty = Bounty(
            host.Id,
            "Public bounty",
            BountyVisibility.Public,
            Guid.Parse("0c6f16d8-4494-44c4-94e7-c5c148638b88"),
            now
        );
        publicBounty.Pledges =
        [
            Pledge(host.Id, "earlier", "earlier-user-id", "600", now.AddMinutes(-2)),
            Pledge(host.Id, "latest", "latest-user-id", "300", now.AddMinutes(-1)),
        ];
        var privateBounty = Bounty(
            host.Id,
            "private-bounty",
            BountyVisibility.Private,
            Guid.NewGuid(),
            now
        );
        db.Bounties.AddRange(publicBounty, privateBounty);
        _ = await db.SaveChangesAsync();
        return new ProjectionSeed(
            host.Id,
            otherHost.Id,
            publicGoal.PublicId,
            publicBounty.PublicId
        );
    }

    private static BotHost Host(string login, HostFeatureFlags features) =>
        new()
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = features,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static CommunitySeason Season(
        int hostId,
        string name,
        CommunityVisibility visibility,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            Description = string.Empty,
            ModeratorNotes = "moderator-secret",
            Status = CommunitySeasonStatus.Open,
            Visibility = visibility,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(10),
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

    private static CommunityDefinition Definition(
        int hostId,
        CommunitySeason season,
        string name,
        CommunityProgressScope scope,
        Guid publicId
    ) =>
        new()
        {
            HostId = hostId,
            PublicId = publicId,
            Season = season,
            Key = publicId.ToString("N"),
            Name = name,
            Description = string.Empty,
            Kind = CommunityDefinitionKind.Quest,
            Scope = scope,
            CompletionMode = CommunityCompletionMode.OneTime,
            EventRule = CommunityEventRuleKind.BountyCompleted,
            Increment = CommunityProgressIncrement.Occurrence,
            Target = 4,
            PointsReward = "0",
            ResetCadence = CommunityResetCadence.None,
            ResetLocalTime = "00:00",
            ScheduleRevision = 1,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static Bounty Bounty(
        int hostId,
        string title,
        BountyVisibility visibility,
        Guid publicId,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            PublicId = publicId,
            CreationOperationId = Guid.NewGuid(),
            CreationFingerprint = publicId.ToString("N"),
            Title = title,
            Status = BountyStatus.Funding,
            Visibility = visibility,
            FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
            RewardDistribution = BountyRewardDistribution.Proportional,
            FundingTarget = "1500",
            PledgedAmount = "900",
            ContributorCount = 2,
            CompletionReward = "0",
            ExpiresAtUtc = now.AddDays(2),
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

    private static BountyPledge Pledge(
        int hostId,
        string login,
        string twitchUserId,
        string amount,
        DateTime createdAtUtc
    ) =>
        new()
        {
            HostId = hostId,
            OperationId = Guid.NewGuid(),
            CommandFingerprint = Guid.NewGuid().ToString("N"),
            ContributorTwitchUserId = twitchUserId,
            ContributorLogin = login,
            Amount = amount,
            State = BountyPledgeState.Reserved,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };

    private static async Task SetFeaturesAsync(
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

    private static async Task SetBountyStatusAsync(
        SqliteBlokeBotDbFactory database,
        Guid bountyId,
        BountyStatus status
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var bounty = await db.Bounties.SingleAsync(value => value.PublicId == bountyId);
        bounty.Status = status;
        _ = await db.SaveChangesAsync();
    }

    private static async Task<int> CountGoalsAsync(SqliteBlokeBotDbFactory database, int hostId)
    {
        await using var db = await database.CreateDbContextAsync();
        return await db.CommunityDefinitions.CountAsync(value => value.HostId == hostId);
    }

    private static async Task<OverlayLiveCoordinator.OverlayLiveConnection> OpenAsync(
        OverlayLiveCoordinator coordinator,
        ResolvedOverlayInstance instance
    ) =>
        (await coordinator.OpenAsync(instance, coordinator.Generation, CancellationToken.None))
            .ShouldBeOfType<OverlayLiveOpenResult.Opened>()
            .Connection;

    private static async Task<OverlayLiveTransportMessage> ReadLiveAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await connection.Messages.ReadAsync(timeout.Token);
    }

    private static string ReplaceNumber(string json, string property, int value)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root[property] = value;
        return root.ToJsonString();
    }

    private static string WithNull(string json, string property)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root[property] = null;
        return root.ToJsonString();
    }

    private sealed record ProjectionSeed(
        int HostId,
        int OtherHostId,
        Guid PublicGoalId,
        Guid PublicBountyId
    );

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableProgressProvider : IOverlayStateProvider
    {
        private readonly Dictionary<
            (int HostId, Guid OverlayId),
            ProgressOverlayItemPresentation
        > _items = [];

        internal void Set(
            ResolvedOverlayInstance instance,
            string current,
            ProgressOverlayItemState state,
            int completionCount = 0
        ) =>
            _items[(instance.HostId, instance.OverlayId)] = new(
                Guid.Parse("5cfbd8aa-d207-4fdb-9714-daf7f669c462"),
                "Context",
                "Goal",
                current,
                "100",
                int.Parse(current, System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.UtcNow.AddDays(1),
                state,
                completionCount,
                []
            );

        public Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = new ProgressOverlayPresentationState([
                _items[(instance.HostId, instance.OverlayId)],
            ]);
            return Task.FromResult<OverlaySnapshotProjection>(
                instance.Configuration switch
                {
                    OverlayConfiguration.CommunityGoalV1 configuration =>
                        new OverlaySnapshotProjection.CommunityGoalV1(
                            new CommunityGoalV1OverlaySnapshot
                            {
                                ServerEpoch = Guid.Empty,
                                Sequence = instance.Revision.Value,
                                GeneratedAtUtc = DateTimeOffset.UtcNow,
                                RotationSeconds = configuration.RotationSeconds,
                                Animation = "none",
                                Appearance = configuration.Appearance,
                                State = state,
                            }
                        ),
                    OverlayConfiguration.ViewerFundedBountyV1 configuration =>
                        new OverlaySnapshotProjection.ViewerFundedBountyV1(
                            new ViewerFundedBountyV1OverlaySnapshot
                            {
                                ServerEpoch = Guid.Empty,
                                Sequence = instance.Revision.Value,
                                GeneratedAtUtc = DateTimeOffset.UtcNow,
                                RotationSeconds = configuration.RotationSeconds,
                                Animation = "none",
                                Appearance = configuration.Appearance,
                                State = state,
                            }
                        ),
                    _ => new OverlaySnapshotProjection.Unavailable(),
                }
            );
        }
    }
}
