using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.CommunityProgression;
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
            cut.Markup.ShouldContain("Viewer Name (@viewer)");
            cut.Markup.ShouldContain("Friendly Raider (@raider)");
            cut.Markup.ShouldContain("12 viewers");
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
            cut.FindAll("[data-bingo-game], [data-bingo-square-editors]").ShouldBeEmpty();
        });
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
