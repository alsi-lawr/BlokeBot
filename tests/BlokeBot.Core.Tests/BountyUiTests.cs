using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class BountyUiTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task PublicBoard_ShowsRecordedPublicContributorsAndNoPrivateBountyData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        var service = new BountyService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new FixedTimeProvider(_now)
        );
        var visible = await CreateAndOpenAsync(
            service,
            hostId,
            "Visible bounty",
            BountyVisibility.Public
        );
        _ = Success(
            await service.PledgeAsync(
                hostId,
                new PledgeBountyCommand(
                    Guid.NewGuid(),
                    visible.PublicId,
                    new BountyActor("viewer-id", "recorded_login"),
                    new PointAmount(40)
                ),
                default
            )
        );
        _ = await CreateAndOpenAsync(service, hostId, "PRIVATE-BOUNTY", BountyVisibility.Private);
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        UiTestContextFactory.AddMomentAttachmentServices(context, database);
        _ = context.Services.AddSingleton(service);
        _ = context.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(_now));
        _ = context.AddAuthorization().SetNotAuthorized();

        var cut = context.Render<PublicBountyBoardPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Visible bounty");
            cut.Markup.ShouldContain("@recorded_login");
            cut.Markup.ShouldContain("40");
            cut.Markup.ShouldNotContain("PRIVATE-BOUNTY");
            cut.Markup.ShouldNotContain("moderator");
        });
    }

    [Test]
    public async Task SignedInDirectRoute_WhenDisabled_ShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        var service = new BountyService(
            database,
            TestEventBus.Create<AppEventKind>(),
            new FixedTimeProvider(_now)
        );
        _ = Success(
            await service.CreateAsync(
                hostId,
                Create("RETAINED-PRIVATE", BountyVisibility.Private),
                default
            )
        );
        await using (var disable = await database.CreateDbContextAsync())
        {
            var host = await disable.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.Bounties;
            _ = await disable.SaveChangesAsync();
        }
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var cut = context.Render<BountiesPage>();

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("RETAINED-PRIVATE"));
    }

    [Test]
    public async Task NewBounty_CreatesWithEveryGroupedChoiceAndThePrivateModeratorNote()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        var service = CreateService(database);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var page = RenderAuthoring(context);
        page.Find("#bounty-title").Input("No-reset speedrun");
        page.Find("#bounty-expiry").Input("2026-09-01 16:00");
        page.Find("#bounty-description").Input("Fund a no-reset attempt tonight.");
        page.Find("#bounty-target").Input("1500");
        page.Find("#bounty-reward").Input("300");
        page.Find("#bounty-visibility").Change(nameof(BountyVisibility.Private));
        page.Find("[data-failure-policy='Spend']").Click();
        page.Find("[data-reward-distribution='Equal']").Click();
        OpenModeratorNote(page);
        page.Find("#bounty-create-reason").Input("PRIVATE-CREATION-NOTE");
        page.Find(".bounty-authoring__submit button").Click();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Created proposed bounty"));
        var created = (
            await service.GetModeratorBoardAsync(hostId, default)
        ).ShouldHaveSingleItem();
        created.Bounty.Title.ShouldBe("No-reset speedrun");
        created.Bounty.Description.ShouldBe("Fund a no-reset attempt tonight.");
        created.Bounty.FundingTarget.ShouldBe(new PointAmount(1500));
        created.Bounty.CompletionReward.ShouldBe(new PointAmount(300));
        created.Bounty.ExpiresAtUtc.ShouldBe(new DateTime(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc));
        created.Bounty.Visibility.ShouldBe(BountyVisibility.Private);
        created.Bounty.FailurePledgePolicy.ShouldBe(BountyFailurePledgePolicy.Spend);
        created.Bounty.RewardDistribution.ShouldBe(BountyRewardDistribution.Equal);
        created.Audits.ShouldContain(audit => audit.Reason == "PRIVATE-CREATION-NOTE");
    }

    [Test]
    public async Task ModeratorNote_IsAKeyboardDisclosureThatStaysCollapsedByDefault()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(CreateService(database));

        var page = RenderAuthoring(context);

        var toggle = page.Find("[data-fold='bounty-moderator-note'] button");
        toggle.GetAttribute("type").ShouldBe("button");
        toggle.GetAttribute("aria-expanded").ShouldBe("false");
        var bodyId = toggle.GetAttribute("aria-controls");
        bodyId.ShouldNotBeNullOrWhiteSpace();
        page.Find($"#{bodyId}").HasAttribute("inert").ShouldBeTrue();

        OpenModeratorNote(page);

        page.Find($"#{bodyId}").HasAttribute("inert").ShouldBeFalse();
    }

    private static BountyService CreateService(SqliteBlokeBotDbFactory database) =>
        new(database, TestEventBus.Create<AppEventKind>(), new FixedTimeProvider(_now));

    private static IRenderedComponent<BountiesPage> RenderAuthoring(BunitContext context)
    {
        var page = context.Render<BountiesPage>();
        page.WaitForAssertion(() =>
            _ = page.Find("[data-stage='bounty-new'] .studio-stage__header")
        );
        page.Find("[data-stage='bounty-new'] .studio-stage__header").Click();
        page.WaitForAssertion(() => _ = page.Find(".bounty-authoring__submit button"));
        return page;
    }

    private static void OpenModeratorNote(IRenderedComponent<BountiesPage> page)
    {
        page.Find("[data-fold='bounty-moderator-note'] button").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-fold='bounty-moderator-note'] button")
                .GetAttribute("aria-expanded")
                .ShouldBe("true")
        );
    }

    private static async Task<BountyView> CreateAndOpenAsync(
        BountyService service,
        int hostId,
        string title,
        BountyVisibility visibility
    )
    {
        var bounty = Success(
            await service.CreateAsync(hostId, Create(title, visibility), default)
        ).Value;
        return Success(
            await service.TransitionAsync(
                hostId,
                new TransitionBountyCommand(
                    Guid.NewGuid(),
                    bounty.PublicId,
                    bounty.Revision,
                    BountyTransitionAction.OpenFunding,
                    new BountyActor("streamer-id", "streamer"),
                    "PRIVATE-MODERATOR-REASON"
                ),
                default
            )
        ).Value;
    }

    private static CreateBountyCommand Create(string title, BountyVisibility visibility) =>
        new(
            Guid.NewGuid(),
            title,
            "Public description",
            new PointAmount(100),
            _now.AddDays(1).UtcDateTime,
            new PointAmount(5),
            visibility,
            BountyFailurePledgePolicy.Refund,
            BountyRewardDistribution.Equal,
            new BountyActor("streamer-id", "streamer"),
            "PRIVATE-CREATION-REASON"
        );

    private static BountyResult<T>.Succeeded Success<T>(BountyResult<T> result) =>
        result.Match(
            static value => value,
            static rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = features,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = host.Id,
                Login = "recorded_login",
                Amount = "100",
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
