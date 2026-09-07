using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
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

    [Test]
    public async Task AutomationExport_SelectsOnlyChosenFlowAndDropsDeselectedScenarios()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database);
        var flowId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        var services = ConfigurationTransferAutomationTestServices.Create(database);
        var source = new AutomationFlowDraftNode(
            new(Guid.NewGuid()),
            new(
                AutomationDefinitionIds.StreamOnlineSource.Value,
                1,
                JsonSerializer.SerializeToElement(new { })
            ),
            new(1),
            AutomationNodeFailurePolicy.Stop,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
        );
        var draft = new AutomationFlowDraft(
            new(flowId),
            new(hostId),
            "First flow",
            1,
            false,
            [source],
            []
        );
        var scenarios = new AutomationScenarioService(
            database,
            services.Catalog,
            services.Flows,
            TimeProvider.System
        );
        var fixtureJson = AutomationScenarioSerialization.Serialize(
            scenarios.CreatePortableFixture(draft, source.Id)
        );
        await using (var db = await database.CreateDbContextAsync())
        {
            db.AutomationFlows.AddRange(
                new AutomationFlow
                {
                    Id = flowId,
                    HostId = hostId,
                    Name = "First flow",
                    Nodes = [AutomationFlowService.Persist(flowId, source)],
                    SchemaVersion = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                },
                new AutomationFlow
                {
                    Id = otherId,
                    HostId = hostId,
                    Name = "Second flow",
                    Nodes =
                    [
                        AutomationFlowService.Persist(
                            otherId,
                            source with
                            {
                                Id = new(Guid.NewGuid()),
                            }
                        ),
                    ],
                    SchemaVersion = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = db.AutomationScenarios.Add(
                new()
                {
                    Id = scenarioId,
                    FlowId = flowId,
                    Slot = 0,
                    Name = "Generated test",
                    FixtureJson = fixtureJson,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        await using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddBlokeBotConfigurationTransfer();
        var page = context.Render<ConfigurationTransferPage>();
        var overlay = page.FindComponent<ConfigurationTransferOverlayExportOptions>();
        await page.InvokeAsync(() => overlay.Instance.UrlLayersChanged.InvokeAsync(false));
        page.Find("button[aria-label='Export flow First flow']").Click();
        page.Find("button[aria-label='Export scenario Generated test for First flow']").Click();
        var link = page.Find("#configuration-transfer-download").GetAttribute("href")!;
        link.ShouldContain(flowId.ToString());
        link.ShouldContain(scenarioId.ToString());
        link.ShouldNotContain(otherId.ToString());
        page.Find("button[aria-label='Export flow First flow']").Click();
        link = page.Find("#configuration-transfer-download").GetAttribute("href")!;
        link.ShouldNotContain(flowId.ToString());
        link.ShouldNotContain(scenarioId.ToString());
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
