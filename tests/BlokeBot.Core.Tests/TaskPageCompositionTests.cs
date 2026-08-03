using BlokeBot.Core.Features.Home;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Persistence.Models;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TaskPageCompositionTests
{
    [Test]
    public async Task HomePage_InformationCardsAreWholeLinksToExactChannelSetupTargets()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.ComponentFactories.AddStub<PublicLeaderboardPrompt>();

        var page = context.Render<HomePage>();

        page.FindAll("a.home-info-card")
            .Select(static link =>
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
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
