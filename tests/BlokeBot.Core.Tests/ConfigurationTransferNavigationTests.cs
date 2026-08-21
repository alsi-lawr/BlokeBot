using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferNavigationTests
{
    [Test]
    public async Task CancelImport_ReturnsToEmptyImportAtImportFragment()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddBlokeBotConfigurationTransfer();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        navigation.NavigateTo("configuration-transfer#import");
        var page = context.Render<ConfigurationTransferPage>();

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Cancel").Click();

        navigation.Uri.ShouldEndWith("/configuration-transfer#import");
        _ = page.Find("#configuration-transfer-json");
        page.Find("[aria-selected='true']").TextContent.Trim().ShouldBe("Import");
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
