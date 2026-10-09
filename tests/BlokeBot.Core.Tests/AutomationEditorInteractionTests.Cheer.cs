using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task Cheer_InspectorSaveReopenClearAndInvalidRecoveryPreserveTheAuthoredBounds()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var page = fixture.Page;
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationWorkspaceToolbar>().Instance.ToggleToolbox.InvokeAsync()
        );
        var toolbox = page.FindComponent<AutomationToolbox>();
        var definition = toolbox.Instance.Definitions.Single(value =>
            value.Id == AutomationDefinitionIds.CheerSource
        );
        await page.InvokeAsync(() => toolbox.Instance.Add.InvokeAsync(definition));
        var cheer = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id == AutomationDefinitionIds.CheerSource
            );
        await DiscloseAsync(page, cheer.Id);
        page.Find("input[id$='-maximum-bits']").GetAttribute("value").ShouldBe(string.Empty);
        page.Find("input[id$='-minimum-bits']").Input("100");
        await SaveAndReopenAsync();
        (await ConfigurationAsync()).TryGetProperty("maximum-bits", out _).ShouldBeFalse();

        page.Find("input[id$='-maximum-bits']").Input("200");
        await SaveAndReopenAsync();
        page.Find("input[id$='-maximum-bits']").GetAttribute("value").ShouldBe("200");
        (await ConfigurationAsync()).GetProperty("maximum-bits").GetInt32().ShouldBe(200);

        foreach (var invalid in new[] { "99", "100.5", "2147483648", "9223372036854775808" })
        {
            page.Find("input[id$='-maximum-bits']").Input(invalid);
            page.Find("[data-automation-save-flow]").Click();
            page.WaitForAssertion(() =>
                page.FindComponent<AutomationNodeInspector>()
                    .Instance.Errors.ShouldContain(error =>
                        error.NodeId == cheer.Id
                        && error.FieldId == new AutomationConfigurationFieldId("maximum-bits")
                    )
            );
            page.Find("input[id$='-maximum-bits']").GetAttribute("value").ShouldBe(invalid);
            page.Find("input[id$='-maximum-bits']").GetAttribute("aria-invalid").ShouldBe("true");
            (await ConfigurationAsync()).GetProperty("maximum-bits").GetInt32().ShouldBe(200);
            page.Find("input[id$='-maximum-bits']").Input("200");
            await SaveAndReopenAsync();
        }

        page.Find("input[id$='-maximum-bits']").Input(string.Empty);
        await SaveAndReopenAsync();
        page.Find("input[id$='-maximum-bits']").GetAttribute("value").ShouldBe(string.Empty);
        (await ConfigurationAsync()).TryGetProperty("maximum-bits", out _).ShouldBeFalse();
        (await ConfigurationAsync()).GetProperty("minimum-bits").GetInt32().ShouldBe(100);

        async Task SaveAndReopenAsync()
        {
            page.Find("[data-automation-save-flow]").Click();
            page.WaitForAssertion(() =>
                page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
            );
            var rail = page.FindComponent<AutomationFlowRail>();
            await page.InvokeAsync(() =>
                rail.Instance.Select.InvokeAsync(rail.Instance.Flows.Single())
            );
            await DiscloseAsync(page, cheer.Id);
        }

        async Task<JsonElement> ConfigurationAsync()
        {
            await using var db = await fixture.Database.CreateDbContextAsync();
            var json = await db
                .AutomationFlowNodes.Where(node => node.Id == cheer.Id.Value)
                .Select(node => node.ConfigurationJson)
                .SingleAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
    }
}
