using BlokeBot.Core.Features.Home;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class TaskPageCompositionTests
{
    [Test]
    public async Task HomePage_InformationCardsAreWholeLinksToExactChannelSetupTargets()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        context.ComponentFactories.AddStub<PublicLeaderboardPrompt>();

        var page = context.Render<HomePage>();

        page.FindAll("a.home-info-card")
            .Select(link =>
                (link.QuerySelector("h2")?.TextContent.Trim(), link.GetAttribute("href"))
            )
            .ShouldBe([
                ("Set up your channel", "/host"),
                ("Choose your chat tools", "/host#chat-tools"),
                ("Let trusted mods help", "/host#moderator-help"),
                ("Keep the bot ready", "/host#bot-status"),
            ]);
        page.FindAll("article.home-info-card").ShouldBeEmpty();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }
}
