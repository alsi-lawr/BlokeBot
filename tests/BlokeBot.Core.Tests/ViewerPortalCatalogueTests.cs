using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalCatalogueTests
{
    [Test]
    public async Task DependencyDisableAndPrivateBounty_DoNotCreatePublicDataOrNavigation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var host = await context.HostAsync(
            "alpha",
            HostFeatureFlags.Bounties | HostFeatureFlags.Points
        );
        var other = await context.HostAsync(
            "beta",
            HostFeatureFlags.Bounties | HostFeatureFlags.Points
        );
        _ = (
            await context.Bounties.CreateAsync(
                host,
                Bounty(context) with
                {
                    Visibility = BountyVisibility.Private,
                },
                default
            )
        ).ShouldBeOfType<BountyResult<BountyView>.Succeeded>();
        _ = (
            await context.Bounties.CreateAsync(
                other,
                Bounty(context) with
                {
                    Title = "Other host challenge",
                },
                default
            )
        ).ShouldBeOfType<BountyResult<BountyView>.Succeeded>();
        var channel = await context.ChannelAsync("alpha");

        var admitted = (
            await context.Bounties.GetPublicBoardAsync(host, default)
        ).ShouldBeOfType<BountyPublicBoardOutcome.Available>();
        admitted.Bounties.ShouldBeEmpty();
        var empty = await context.Catalogue.ReadAsync(
            channel,
            new PortalIdentity.Anonymous(),
            default
        );
        _ = Feature(empty, HostFeatureFlags.Bounties).ShouldBeOfType<PortalSummaryOutcome.Empty>();
        var featureService = TestHostFeatureServices.Create(
            database,
            context.Changes,
            [],
            context.Clock
        );
        _ = await featureService.DisableAsync(host, HostFeatureFlags.Points, default);
        (
            await featureService.IsEnabledAsync(host, HostFeatureFlags.Bounties, default)
        ).ShouldBeTrue();

        _ = (
            await context.Bounties.GetPublicBoardAsync(host, default)
        ).ShouldBeOfType<BountyPublicBoardOutcome.Disabled>();
        var disabled = await context.Catalogue.ReadAsync(
            channel,
            new PortalIdentity.Anonymous(),
            default
        );
        disabled.Features.ShouldBeEmpty();
        disabled.RecentActivity.ShouldBeEmpty();
    }

    [Test]
    public async Task PublicCollectives_ExcludePendingAndDisabledMembershipsAndUseOpaqueHostLinks()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var alpha = await context.HostAsync("alpha", HostFeatureFlags.Collectives);
        var beta = await context.HostAsync("beta", HostFeatureFlags.Collectives);
        var authority = new CollectiveAuthority(alpha, "alpha-id", "alpha", true);
        var created = (
            await context.Collectives.CreateAsync(
                new(Guid.NewGuid(), "Friends", authority),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        _ = (
            await context.Collectives.InviteAsync(
                new(Guid.NewGuid(), created.CollectiveId, beta, authority),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();
        var channel = await context.ChannelAsync("beta");
        (await context.Collectives.GetPublicListingsAsync(beta, default)).ShouldBeEmpty();
        var pending = await context.Catalogue.ReadAsync(
            channel,
            new PortalIdentity.Anonymous(),
            default
        );
        Feature(pending, HostFeatureFlags.Collectives)
            .ShouldBeOfType<PortalSummaryOutcome.Empty>()
            .Summary.Links.ShouldBeEmpty();
        _ = (
            await context.Collectives.AcceptInvitationAsync(
                new(
                    Guid.NewGuid(),
                    created.CollectiveId,
                    new CollectiveAuthority(beta, "beta-id", "beta", true)
                ),
                default
            )
        ).ShouldBeOfType<CollectiveMutationOutcome.Succeeded>();

        var active = await context.Catalogue.ReadAsync(
            channel,
            new PortalIdentity.Anonymous(),
            default
        );
        var link = Feature(active, HostFeatureFlags.Collectives)
            .ShouldBeOfType<PortalSummaryOutcome.Available>()
            .Summary.Links.Single();
        link.Href.ShouldBe($"/collectives/beta/{created.CollectiveId.Value:D}");
        _ = (
            await context.Collectives.LoadPublicAsync("beta", created.CollectiveId, default)
        ).ShouldNotBeNull();
        _ = await TestHostFeatureServices
            .Create(database, context.Changes, [], context.Clock)
            .DisableAsync(beta, HostFeatureFlags.Collectives, default);
        (await context.Collectives.GetPublicListingsAsync(beta, default)).ShouldBeEmpty();
        (
            await context.Catalogue.ReadAsync(channel, new PortalIdentity.Anonymous(), default)
        ).Features.ShouldBeEmpty();
    }

    [Test]
    public async Task OpenDirectories_UseStoredHostSlugsAndBoundDetailLinksWithoutClosedOrOtherHostData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var alpha = await context.HostAsync(
            "alpha",
            HostFeatureFlags.PlayWithViewers | HostFeatureFlags.RequestBoards
        );
        var beta = await context.HostAsync(
            "beta",
            HostFeatureFlags.PlayWithViewers | HostFeatureFlags.RequestBoards
        );
        for (var i = 0; i < 7; i++)
        {
            _ = (
                await context.Queues.ConfigureAsync(
                    alpha,
                    Queue($"  GAME-{i}  ") with
                    {
                        Name = $"Queue {i}",
                    },
                    default
                )
            ).ShouldBeOfType<PlayQueueResult<PlayQueueSummary>.Succeeded>();
            _ = (
                await context.Requests.ConfigureAsync(
                    alpha,
                    Board($"  SONG-{i}  ") with
                    {
                        Title = $"Board {i}",
                    },
                    default
                )
            ).ShouldBeOfType<RequestBoardResult<RequestBoardSummary>.Succeeded>();
        }
        _ = await context.Queues.ConfigureAsync(
            alpha,
            Queue("closed") with
            {
                Name = "A closed queue",
                IsOpen = false,
            },
            default
        );
        _ = await context.Requests.ConfigureAsync(
            alpha,
            Board("closed") with
            {
                Title = "A closed board",
                IsOpen = false,
            },
            default
        );
        _ = await context.Queues.ConfigureAsync(beta, Queue("other"), default);
        _ = await context.Requests.ConfigureAsync(beta, Board("other"), default);

        var snapshot = await context.Catalogue.ReadAsync(
            await context.ChannelAsync("@ALPHA"),
            new PortalIdentity.Anonymous(),
            default
        );
        var queues = Feature(snapshot, HostFeatureFlags.PlayWithViewers)
            .ShouldBeOfType<PortalSummaryOutcome.Available>()
            .Summary.Links;
        var boards = Feature(snapshot, HostFeatureFlags.RequestBoards)
            .ShouldBeOfType<PortalSummaryOutcome.Available>()
            .Summary.Links;
        queues.Length.ShouldBe(5);
        boards.Length.ShouldBe(5);
        for (var i = 0; i < 5; i++)
        {
            queues[i].Href.ShouldBe($"/queues/alpha/game-{i}");
            boards[i].Href.ShouldBe($"/requests/alpha/song-{i}");
            _ = (
                await context.Queues.GetPublicPageAsync("alpha", $"game-{i}", default)
            ).ShouldNotBeNull();
            _ = (
                await context.Requests.GetPublicPageAsync("alpha", $"song-{i}", default)
            ).ShouldNotBeNull();
        }
    }

    [Test]
    public async Task PublicGameLeaderAndSelfPassport_KeepResultsSeparateFromPrivateProfile()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var alpha = await context.HostAsync(
            "alpha",
            HostFeatureFlags.Points | HostFeatureFlags.ViewerPassports
        );
        var beta = await context.HostAsync(
            "beta",
            HostFeatureFlags.Points | HostFeatureFlags.ViewerPassports
        );
        await context.PointsAsync(alpha, "hidden", "100");
        await context.PointsAsync(alpha, "visible", "10");
        await context.PointsAsync(beta, "other-host", "1000");
        _ = (
            await context.Passports.SaveAsync(
                new(
                    alpha,
                    new("hidden-id", "hidden", "Hidden"),
                    "Private biography",
                    ViewerPassportVisibility.Private,
                    true,
                    null,
                    null
                ),
                default
            )
        ).ShouldBeOfType<ViewerPassportMutationOutcome.Succeeded>();
        var channel = await context.ChannelAsync("alpha");

        var anonymous = await context.Catalogue.ReadAsync(
            channel,
            new PortalIdentity.Anonymous(),
            default
        );
        Feature(anonymous, HostFeatureFlags.Points)
            .ShouldBeOfType<PortalSummaryOutcome.Available>()
            .Summary.Headline.ShouldBe("hidden");
        anonymous.Features.ShouldNotContain(value =>
            value.Descriptor.Feature == HostFeatureFlags.ViewerPassports
        );
        _ = anonymous.CacheScope.ShouldBeOfType<PortalCacheScope.Public>();
        var authenticated = await context.Catalogue.ReadAsync(
            channel,
            new PortalIdentity.Authenticated("hidden-id", "hidden", "Hidden"),
            default
        );
        var passport = Feature(authenticated, HostFeatureFlags.ViewerPassports)
            .ShouldBeOfType<PortalSummaryOutcome.Available>()
            .Summary;
        passport.Detail.ShouldBe("Private biography");
        passport.Links.Single().Href.ShouldBe("/passports/alpha/me");
        _ = authenticated.CacheScope.ShouldBeOfType<PortalCacheScope.Private>();
        authenticated.RecentActivity.ShouldBeEmpty();
    }

    [Test]
    public async Task RecentPublicMoments_AreHostScopedNewestFirstAndBoundedAcrossTheMergedFeed()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var alpha = await context.HostAsync("alpha", HostFeatureFlags.Moments);
        var beta = await context.HostAsync("beta", HostFeatureFlags.Moments);
        for (var i = 0; i < 7; i++)
        {
            await PublishMomentAsync(context, alpha, $"Moment {i}");
        }
        await PublishMomentAsync(context, beta, "Other host moment");
        var snapshot = await context.Catalogue.ReadAsync(
            await context.ChannelAsync("alpha"),
            new PortalIdentity.Anonymous(),
            default
        );
        snapshot.RecentActivity.Length.ShouldBe(5);
        snapshot
            .RecentActivity.Select(value => value.Description)
            .ShouldBe(["Moment 6", "Moment 5", "Moment 4", "Moment 3", "Moment 2"]);
        snapshot.RecentActivity.ShouldAllBe(value => value.Link.Href == "/moments/alpha");
        var otherFeature = new PortalActivity(
            snapshot.RecentActivity[0].OccurredAtUtc.AddMinutes(1),
            "Bounty completed",
            new("Bounties", "/bounties/alpha")
        );
        var merged = PortalSummaryBounds.Merge(snapshot.RecentActivity.Append(otherFeature));
        merged.Length.ShouldBe(5);
        merged[0].ShouldBe(otherFeature);
        PortalSummaryBounds
            .Merge(snapshot.RecentActivity.Reverse().Append(otherFeature))
            .ShouldBe(merged);
    }

    private static PortalSummaryOutcome Feature(
        PortalCatalogueSnapshot snapshot,
        HostFeatureFlags feature
    ) => snapshot.Features.Single(value => value.Descriptor.Feature == feature).Outcome;

    private static CreateBountyCommand Bounty(ViewerPortalTestContext context) =>
        new(
            Guid.NewGuid(),
            "Secret challenge",
            "Private details",
            new PointAmount(100),
            context.Clock.GetUtcNow().AddDays(1).UtcDateTime,
            new PointAmount(10),
            BountyVisibility.Public,
            BountyFailurePledgePolicy.Refund,
            BountyRewardDistribution.Proportional,
            new BountyActor("alpha-id", "alpha")
        );

    private static ConfigurePlayQueueCommand Queue(string slug) =>
        new(
            slug,
            "Queue",
            "Game",
            4,
            true,
            PlayQueueSelectionMode.LeastRecentParticipation,
            false,
            120,
            30,
            15,
            [],
            []
        );

    private static ConfigureRequestBoardCommand Board(string slug) =>
        new(
            slug,
            "Requests",
            "Suggestions",
            true,
            "0",
            RequestBoardRefundPolicy.RejectedOrWithdrawn,
            3,
            0,
            10,
            true,
            [
                new RequestBoardFieldCommand(
                    "details",
                    "Details",
                    RequestBoardFieldKind.Text,
                    false,
                    500
                ),
            ]
        );

    private static async Task PublishMomentAsync(
        ViewerPortalTestContext context,
        int host,
        string title
    )
    {
        var captured = (
            await context.Moments.CaptureAsync(
                host,
                new CaptureMomentCommand("stream", new("viewer", "viewer-id", "Viewer"), title),
                default
            )
        )
            .ShouldBeOfType<MomentResult<MomentView>.Succeeded>()
            .Value;
        _ = (
            await context.Moments.ApproveAsync(
                host,
                new(captured.PublicId, title, "Gameplay", "host"),
                default
            )
        ).ShouldBeOfType<MomentResult<ModeratorMomentView>.Succeeded>();
        context.Clock.Advance(TimeSpan.FromMinutes(3));
    }
}
