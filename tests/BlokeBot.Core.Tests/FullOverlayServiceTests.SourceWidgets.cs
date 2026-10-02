using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task SourceWidgets_ReuseLivePublicQueueGiveawayAndProgressOwners()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var queueService = new PlayQueueService(
            fixture.Database,
            fixture.Events,
            environment.Clock
        );
        var queue = (
            await queueService.ConfigureAsync(
                fixture.HostId,
                new(
                    "squad",
                    "Squad",
                    "Game",
                    4,
                    true,
                    PlayQueueSelectionMode.JoinOrder,
                    true,
                    120,
                    30,
                    15,
                    [],
                    []
                ),
                _ct
            )
        )
            .ShouldBeOfType<PlayQueueResult<PlayQueueSummary>.Succeeded>()
            .Value;
        _ = (
            await queueService.JoinAsync(
                fixture.HostId,
                queue.Slug,
                new(
                    new("viewer", "private-twitch-id", "Public viewer"),
                    0,
                    new Dictionary<string, string>()
                ),
                _ct
            )
        ).ShouldBeOfType<PlayQueueResult<PublicPlayQueueEntryView>.Succeeded>();
        Guid goalId;
        Guid bountyId;
        await using (var db = fixture.Database.CreateDbContext())
        {
            var entry = await db.PlayQueueEntries.SingleAsync();
            entry.PrivateModeratorNote = "private-queue-note";
            _ = db.PointsGiveaways.Add(
                new()
                {
                    HostId = fixture.HostId,
                    Status = PointsGiveawayStatus.Active,
                    StartedAtUtc = environment.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
                    EndsAtUtc = environment.Clock.GetUtcNow().UtcDateTime.AddMinutes(5),
                    Entrants = [new() { Login = "private-entrant", JoinedAtUtc = DateTime.UtcNow }],
                }
            );
            var season = new CommunitySeason
            {
                HostId = fixture.HostId,
                PublicId = Guid.NewGuid(),
                CreationOperationId = Guid.NewGuid(),
                Name = "Public season",
                ModeratorNotes = "private-season-note",
                Status = CommunitySeasonStatus.Open,
                Visibility = CommunityVisibility.Public,
                StartsAtUtc = DateTime.UtcNow.AddDays(-1),
                EndsAtUtc = DateTime.UtcNow.AddDays(1),
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
            };
            goalId = Guid.NewGuid();
            var goal = new CommunityDefinition
            {
                HostId = fixture.HostId,
                PublicId = goalId,
                Season = season,
                Key = "goal",
                Name = "Public goal",
                Kind = CommunityDefinitionKind.Quest,
                Scope = CommunityProgressScope.Communal,
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
            _ = db.CommunityDefinitions.Add(goal);
            bountyId = Guid.NewGuid();
            _ = db.Bounties.Add(
                new()
                {
                    HostId = fixture.HostId,
                    PublicId = bountyId,
                    CreationOperationId = Guid.NewGuid(),
                    CreationFingerprint = bountyId.ToString("N"),
                    Title = "Public bounty",
                    Status = BountyStatus.Funding,
                    Visibility = BountyVisibility.Public,
                    FailurePledgePolicy = BountyFailurePledgePolicy.Refund,
                    RewardDistribution = BountyRewardDistribution.Proportional,
                    FundingTarget = "100",
                    PledgedAmount = "50",
                    ContributorCount = 1,
                    CompletionReward = "0",
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                    Revision = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Pledges =
                    [
                        new()
                        {
                            HostId = fixture.HostId,
                            OperationId = Guid.NewGuid(),
                            CommandFingerprint = "pledge",
                            ContributorLogin = "public-contributor",
                            ContributorTwitchUserId = "private-contributor-id",
                            Amount = "50",
                            State = BountyPledgeState.Reserved,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow,
                        },
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
            _ = db.CommunityProgress.Add(
                new()
                {
                    HostId = fixture.HostId,
                    SeasonId = season.Id,
                    DefinitionId = goal.Id,
                    SubjectKey = "communal",
                    Amount = 2,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var queueWidget = environment.Registry.Create(new("viewer-queue"), Guid.NewGuid())! with
        {
            Configuration = Config(new OverlayConfiguration.ViewerQueueV1(queue.Id, 0, 1)),
        };
        var giveaway = environment.Registry.Create(new("giveaway"), Guid.NewGuid())!;
        var goalWidget = environment.Registry.Create(new("community-goal"), Guid.NewGuid())! with
        {
            Configuration = Config(new OverlayConfiguration.CommunityGoalV1(goalId, 20, 0)),
        };
        var bounty = environment.Registry.Create(new("viewer-funded-bounty"), Guid.NewGuid())! with
        {
            Configuration = Config(new OverlayConfiguration.ViewerFundedBountyV1(bountyId, 20, 1)),
        };
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            Guid.NewGuid(),
            null,
            null,
            Document() with
            {
                Widgets = [queueWidget, giveaway, goalWidget, bounty],
            },
            FullOverlayDataMode.Live,
            []
        );
        var outputs = await environment.Registry.ProjectAsync(context, _ct);
        OverlaySnapshotProjection Source(int index) =>
            outputs[index]
                .Output.Match<OverlaySnapshotProjection?>(
                    static source => source.Projection,
                    static _ => null,
                    static _ => null,
                    static _ => null,
                    static _ => null,
                    static _ => null,
                    static _ => null
                )
                .ShouldNotBeNull();
        var queueSnapshot = Source(0)
            .ShouldBeOfType<OverlaySnapshotProjection.ViewerQueueV1>()
            .Snapshot;
        var giveawaySnapshot = Source(1)
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot;
        var goalSnapshot = Source(2)
            .ShouldBeOfType<OverlaySnapshotProjection.CommunityGoalV1>()
            .Snapshot;
        var bountySnapshot = Source(3)
            .ShouldBeOfType<OverlaySnapshotProjection.ViewerFundedBountyV1>()
            .Snapshot;
        queueSnapshot.State.Next.Single().DisplayName.ShouldBe("Public viewer");
        giveawaySnapshot
            .State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Open>()
            .EntrantCount.ShouldBe(1);
        goalSnapshot.State.Items.Single().Percentage.ShouldBe(50);
        bountySnapshot
            .State.Items.Single()
            .RecentContributors.Single()
            .Login.ShouldBe("public-contributor");
        var json = JsonSerializer.Serialize(
            new
            {
                Queue = queueSnapshot,
                Giveaway = giveawaySnapshot,
                Goal = goalSnapshot,
                Bounty = bountySnapshot,
            }
        );
        json.ShouldContain("Public viewer");
        json.ShouldContain("Public goal");
        json.ShouldContain("Public bounty");
        json.ShouldContain("public-contributor");
        using var payload = JsonDocument.Parse(json);
        payload
            .RootElement.GetProperty("Giveaway")
            .GetProperty("State")
            .GetProperty("EntrantCount")
            .GetInt32()
            .ShouldBe(1);
        json.ShouldNotContain("private-queue-note");
        json.ShouldNotContain("private-twitch-id");
        json.ShouldNotContain("private-entrant");
        json.ShouldNotContain("private-season-note");
        json.ShouldNotContain("private-contributor-id");
    }

    [Test]
    public async Task CueAndWebContracts_RepeatScopedPlanWithIndependentAudioAndIsolateFailedWidget()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture, dns: new FailedDns());
        var cue = environment.Registry.Create(new("cue-player"), Guid.NewGuid())!;
        var copy = cue with { Id = new(Guid.NewGuid()), Audio = new(true, 0.4) };
        var web = environment.Registry.Create(new("web"), Guid.NewGuid())!;
        var overlayId = Guid.NewGuid();
        var plan = new OverlayCuePlaybackPlan(
            Guid.NewGuid(),
            fixture.HostId,
            overlayId,
            Guid.NewGuid(),
            1,
            1000,
            OverlayCueAdmissionOrigin.OwnerPreview,
            OverlayCueSafeContext.Empty,
            [
                new OverlayCuePlaybackLayer.RemoteMedia
                {
                    StartOffsetMilliseconds = 0,
                    DurationMilliseconds = 1000,
                    ZIndex = 1,
                    Rectangle = new(0, 0, 400, 300),
                    Url = new("https://example.com/video.webm"),
                    MediaKind = OverlayCueMediaKind.Video,
                    Volume = 0.5m,
                    Fit = OverlayCueFitMode.Contain,
                },
            ]
        );
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            overlayId,
            null,
            null,
            Document() with
            {
                Widgets = [web, cue, copy],
            },
            FullOverlayDataMode.Live,
            [plan, plan with { HostId = fixture.OtherHostId, RunId = Guid.NewGuid() }]
        );
        var projections = await environment.Registry.ProjectAsync(context, _ct);
        projections[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>()
            .Diagnostic.Message.ShouldNotContain("PRIVATE");
        projections[1]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.CuePlayer>()
            .Plans.ShouldBe([plan]);
        projections[2]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.CuePlayer>()
            .Plans.ShouldBe([plan]);
        projections[1].Audio.ShouldBe(cue.Audio);
        projections[2].Audio.ShouldBe(copy.Audio);
        context.CuePlans.Length.ShouldBe(2);
    }

    private sealed class FailedDns : IOverlayDnsResolver
    {
        public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(
            string host,
            CancellationToken ct
        ) => throw new InvalidOperationException("PRIVATE infrastructure detail");
    }
}
