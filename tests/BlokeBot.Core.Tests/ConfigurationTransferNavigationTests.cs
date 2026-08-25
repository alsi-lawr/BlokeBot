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
    public async Task OverlayExportChildren_AreUnavailableWithParentAndRemainIndependent()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddBlokeBotConfigurationTransfer();
        var page = context.Render<ConfigurationTransferPage>();
        var overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeFalse();

        await page.InvokeAsync(() => overlay.Instance.MediaDocumentLinksChanged.InvokeAsync(false));
        overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        overlay.Instance.MediaDocumentLinks.ShouldBeFalse();
        overlay.Instance.UrlLayers.ShouldBeTrue();
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeFalse();

        await page.InvokeAsync(() => overlay.Instance.UrlLayersChanged.InvokeAsync(false));
        overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        overlay.Instance.UrlLayers.ShouldBeFalse();
        overlay.Instance.MediaDocumentLinks.ShouldBeFalse();
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeTrue();

        var selectedChanged = overlay.Instance.SelectedChanged;
        await page.InvokeAsync(() => selectedChanged.InvokeAsync(false));
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeTrue();

        await page.InvokeAsync(() => selectedChanged.InvokeAsync(true));
        overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        overlay.Instance.UrlLayers.ShouldBeFalse();
        overlay.Instance.MediaDocumentLinks.ShouldBeFalse();

        await page.InvokeAsync(() => overlay.Instance.MediaDocumentLinksChanged.InvokeAsync(true));
        overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        overlay.Instance.UrlLayers.ShouldBeFalse();
        overlay.Instance.MediaDocumentLinks.ShouldBeTrue();
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeTrue();

        await page.InvokeAsync(() => overlay.Instance.UrlLayersChanged.InvokeAsync(true));
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeFalse();
        overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        await page.InvokeAsync(() =>
            overlay.Instance.UrlWarningAcknowledgedChanged.InvokeAsync(true)
        );
        page.FindComponent<ConfigurationTransferOverlayExportOptions>()
            .Instance.UrlWarningAcknowledged.ShouldBeTrue();
        page.Find("#configuration-transfer-download").HasAttribute("href").ShouldBeTrue();
    }

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

        page.Find("#configuration-transfer-cancel").Click();

        navigation.Uri.ShouldEndWith("/configuration-transfer#import");
        _ = page.Find("#configuration-transfer-json");
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
