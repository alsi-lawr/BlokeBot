using AngleSharp.Dom;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BingoUiTests
{
    [Test]
    public async Task PublicCards_ShowNormalizedIdentityEvidenceAndNeverPrivateNotes()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.Bingo);
        var service = Service(database);
        var squares = Enumerable
            .Range(1, 9)
            .Select(value =>
                value == 1
                    ? (BingoSquareDefinition)
                        new BingoSquareDefinition.IncomingRaid(
                            new("raid"),
                            "A raid arrives",
                            1,
                            "PRIVATE-SQUARE-NOTE"
                        )
                    : new BingoSquareDefinition.Manual(
                        new($"manual-{value}"),
                        $"Manual {value}",
                        "PRIVATE-SQUARE-NOTE"
                    )
            )
            .ToArray();
        _ = Success(
            await service.SaveTemplateAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    null,
                    "Public Bingo",
                    new(3),
                    squares,
                    false,
                    BingoWinReward.None,
                    BingoWinReward.None,
                    new("streamer-id", "streamer")
                ),
                default
            )
        );
        var template = (await service.GetTemplatesAsync(hostId, default)).Single();
        _ = Success(
            await service.CreateGameAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    template.Id,
                    BingoGameMode.Shared,
                    "public-seed",
                    null,
                    null,
                    [],
                    new("streamer-id", "streamer")
                ),
                default
            )
        );
        var game = (await service.GetModeratorGamesAsync(hostId, default)).Single().Game;
        var viewer = new BingoViewer("viewer-id", "viewer", "Viewer Name");
        _ = Success(
            await service.JoinAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    game.Id,
                    viewer,
                    null,
                    new("viewer-id", "viewer"),
                    "PRIVATE-ROSTER-NOTE"
                ),
                default
            )
        );
        _ = Success(
            await service.IssueAsync(
                hostId,
                new(Guid.NewGuid(), game.Id, new("streamer-id", "streamer"), "PRIVATE-ISSUE-NOTE"),
                default
            )
        );
        _ = Success(
            await service.ProcessEventAsync(
                hostId,
                new BingoAutomaticEvent.IncomingRaid(
                    "raid-message",
                    new("raider-id", "raider", "Friendly Raider"),
                    12,
                    DateTimeOffset.UtcNow
                ),
                default
            )
        );
        using var context = new BunitContext();
        _ = context.Services.AddSingleton(service);
        _ = context.AddAuthorization().SetNotAuthorized();

        var cut = context.Render<PublicBingoPage>(parameters =>
            parameters.Add(page => page.Channel, "streamer")
        );

        cut.WaitForAssertion(() =>
        {
            _ = cut.Find("[data-bingo-card]");
            cut.Markup.ShouldNotContain("PRIVATE-");
        });
    }

    [Test]
    public async Task SignedInDirectRoute_WhenDisabled_ShowsRecoveryWithoutRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.Bingo);
        var service = Service(database);
        _ = Success(
            await service.SaveTemplateAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    null,
                    "RETAINED-BINGO-TEMPLATE",
                    new(3),
                    Enumerable
                        .Range(1, 9)
                        .Select(value =>
                            (BingoSquareDefinition)
                                new BingoSquareDefinition.Manual(
                                    new($"s-{value}"),
                                    $"Square {value}"
                                )
                        )
                        .ToArray(),
                    false,
                    BingoWinReward.None,
                    BingoWinReward.None,
                    new("streamer-id", "streamer")
                ),
                default
            )
        );
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync();
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var cut = context.Render<BingoPage>();

        cut.WaitForAssertion(() =>
        {
            _ = cut.Find("a[href='/host#chat-tools']");
            cut.Markup.ShouldNotContain("RETAINED-BINGO-TEMPLATE");
        });
    }

    [Test]
    public async Task LoadingASavedRevision_RestoresEveryRewardFieldAndSavesTheNextRevision()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        await SeedExternalAchievementAsync(database, hostId, "line-caller");
        await SeedExternalAchievementAsync(database, hostId, "night-of-moments");
        var service = Service(database);
        _ = Success(
            await service.SaveTemplateAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    null,
                    "Stream moments",
                    new(4),
                    ManualSquares(16),
                    true,
                    new(new PointAmount(50), new("line-caller")),
                    new(new PointAmount(250), new("night-of-moments")),
                    new("streamer-id", "streamer")
                ),
                default
            )
        );
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var page = context.Render<BingoPage>();
        page.WaitForAssertion(() =>
            _ = page.Find("[data-bingo-revisions] .bingo-revisions__meta")
        );
        page.Find(".bingo-revisions__load").Click();

        page.WaitForAssertion(() =>
            page.Find("#bingo-template-name").GetAttribute("value").ShouldBe("Stream moments")
        );
        page.Find("#bingo-dimension").GetAttribute("value").ShouldBe("4");
        page.Find("#bingo-line-points").GetAttribute("value").ShouldBe("50");
        page.Find("#bingo-line-achievement").GetAttribute("value").ShouldBe("line-caller");
        page.Find("#bingo-full-points").GetAttribute("value").ShouldBe("250");
        page.Find("#bingo-full-achievement").GetAttribute("value").ShouldBe("night-of-moments");
        page.Find("#bingo-full-card").GetAttribute("aria-checked").ShouldBe("true");
        page.Find("#bingo-full-achievement").HasAttribute("disabled").ShouldBeFalse();

        page.Find("#bingo-line-points").Input("75");
        SaveRevision(page).Click();

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
        var saved = (await service.GetTemplatesAsync(hostId, default)).ShouldHaveSingleItem();
        saved.Revision.ShouldBe(2);
        saved.Dimension.Value.ShouldBe(4);
        saved.FullCardWinEnabled.ShouldBeTrue();
        saved.LineReward.Points.ShouldBe(new PointAmount(75));
        saved.LineReward.AchievementKey!.Value.Value.ShouldBe("line-caller");
        saved.FullCardReward.Points.ShouldBe(new PointAmount(250));
        saved.FullCardReward.AchievementKey!.Value.Value.ShouldBe("night-of-moments");
        saved.Squares.Count.ShouldBe(16);
    }

    [Test]
    public async Task TurningOffFullCardWins_ClosesTheFullCardAchievementAndPersistsLineRewards()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        await SeedExternalAchievementAsync(database, hostId, "line-caller");
        var service = Service(database);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var page = RenderAuthoring(context);
        page.Find("#bingo-template-name").Input("Line only");
        page.Find("#bingo-dimension").Change("3");
        page.Find("#bingo-line-points").Input("40");
        page.Find("#bingo-line-achievement").Change("line-caller");
        page.Find("#bingo-full-card").Click();

        page.WaitForAssertion(() =>
            page.Find("#bingo-full-achievement").HasAttribute("disabled").ShouldBeTrue()
        );
        page.Find("#bingo-full-card").GetAttribute("aria-checked").ShouldBe("false");
        SaveRevision(page).Click();

        page.WaitForAssertion(() => _ = page.Find("[role='status']"));
        var saved = (await service.GetTemplatesAsync(hostId, default)).ShouldHaveSingleItem();
        saved.Name.ShouldBe("Line only");
        saved.Dimension.Value.ShouldBe(3);
        saved.FullCardWinEnabled.ShouldBeFalse();
        saved.LineReward.Points.ShouldBe(new PointAmount(40));
        saved.LineReward.AchievementKey!.Value.Value.ShouldBe("line-caller");
        saved.FullCardReward.ShouldBe(BingoWinReward.None);
    }

    [Test]
    public async Task ResetSquare_RestoresTheSelectedSquareWithoutTouchingItsNeighbours()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, HostFeatureFlags.All);
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(Service(database));

        var page = RenderAuthoring(context);
        page.FindAll("[data-bingo-square-editors] button")[2].Click();
        page.Find("#bingo-square-title").Input("Third moment");
        page.Find("#bingo-square-key").Input("third-moment");
        page.Find("#bingo-square-kind").Change(nameof(BingoSquareKind.GuessingResult));
        page.WaitForAssertion(() => _ = page.Find("#bingo-square-answer"));
        page.Find("#bingo-square-answer").Input("blue");
        page.Find("#bingo-square-note").Input("PRIVATE-SQUARE-NOTE");

        Button(page, "Reset square").Click();

        page.WaitForAssertion(() =>
            page.Find("#bingo-square-title").GetAttribute("value").ShouldBe("Moment 3")
        );
        page.Find("#bingo-square-key").GetAttribute("value").ShouldBe("square-3");
        page.Find("#bingo-square-kind")
            .GetAttribute("value")
            .ShouldBe(nameof(BingoSquareKind.Manual));
        page.Find("#bingo-square-note").GetAttribute("value").ShouldBe(string.Empty);
        page.FindAll("#bingo-square-answer").ShouldBeEmpty();
        page.Markup.ShouldNotContain("PRIVATE-SQUARE-NOTE");
        page.FindAll("[data-bingo-square-editors] button")[1].TextContent.ShouldContain("Moment 2");
    }

    private static IRenderedComponent<BingoPage> RenderAuthoring(BunitContext context)
    {
        var page = context.Render<BingoPage>();
        page.WaitForAssertion(() => _ = page.Find("[data-bingo-square-editors]"));
        return page;
    }

    private static IElement SaveRevision(IRenderedComponent<BingoPage> page) =>
        page.Find(".sticky-save-region button");

    private static IElement Button(IRenderedComponent<BingoPage> page, string label) =>
        page.FindAll("button").Single(element => element.TextContent.Trim() == label);

    private static IReadOnlyList<BingoSquareDefinition> ManualSquares(int count) =>
        Enumerable
            .Range(1, count)
            .Select(value =>
                (BingoSquareDefinition)
                    new BingoSquareDefinition.Manual(new($"square-{value}"), $"Moment {value}")
            )
            .ToArray();

    private static async Task SeedExternalAchievementAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        string key
    )
    {
        var now = DateTime.UtcNow;
        await using var db = await database.CreateDbContextAsync();
        var season = await db.CommunitySeasons.FirstOrDefaultAsync(value => value.HostId == hostId);
        if (season is null)
        {
            season = new CommunitySeason
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                CreationOperationId = Guid.NewGuid(),
                Name = "Bingo rewards",
                Status = CommunitySeasonStatus.Open,
                Visibility = CommunityVisibility.Public,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddDays(30),
                Revision = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _ = db.CommunitySeasons.Add(season);
        }
        _ = db.CommunityDefinitions.Add(
            new CommunityDefinition
            {
                PublicId = Guid.NewGuid(),
                HostId = hostId,
                Season = season,
                Key = key,
                Name = key,
                Kind = CommunityDefinitionKind.Achievement,
                Scope = CommunityProgressScope.Viewer,
                CompletionMode = CommunityCompletionMode.OneTime,
                EventRule = CommunityEventRuleKind.ExternalGrant,
                Increment = CommunityProgressIncrement.Occurrence,
                Target = 1,
                PointsReward = "0",
                ResetCadence = CommunityResetCadence.None,
                ResetLocalTime = "00:00",
                ScheduleRevision = 1,
                CreatedAtUtc = now,
            }
        );
        _ = await db.SaveChangesAsync();
    }

    private static BingoService Service(SqliteBlokeBotDbFactory database) =>
        new(
            database,
            new CommunityProgressionService(
                database,
                TestEventBus.Create<AppEventKind>(),
                TimeProvider.System
            ),
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );

    private static BingoOperationOutcome.Succeeded Success(BingoOperationOutcome result) =>
        result.ShouldBeOfType<BingoOperationOutcome.Succeeded>();

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
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
