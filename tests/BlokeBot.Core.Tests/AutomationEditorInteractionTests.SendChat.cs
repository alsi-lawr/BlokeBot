using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task SendChat_InspectorMultilineRecoveryAndConnectedSavePreserveFixedTextAndEdges()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var page = fixture.Page;
        var send = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id == AutomationDefinitionIds.SendChatAction
            );
        await DiscloseAsync(page, send.Id);
        page.Find("textarea[id$='-message']").Input(string.Empty);
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>()
                .Instance.Errors.ShouldContain(error =>
                    error.NodeId == send.Id
                    && error.FieldId == new AutomationConfigurationFieldId("message")
                )
        );
        page.Find("textarea[id$='-message']").GetAttribute("aria-invalid").ShouldBe("true");
        const string FixedMessage = "First line\nSecond line ${literal}";
        page.Find("textarea[id$='-message']").Input(FixedMessage);
        await SaveAndReopenAsync();
        page.Find("textarea[id$='-message']").GetAttribute("value").ShouldBe(FixedMessage);

        await page.InvokeAsync(() =>
            page.FindComponent<AutomationWorkspaceToolbar>().Instance.ToggleToolbox.InvokeAsync()
        );
        var toolbox = page.FindComponent<AutomationToolbox>();
        await page.InvokeAsync(() =>
            toolbox.Instance.Add.InvokeAsync(
                toolbox.Instance.Definitions.Single(definition =>
                    definition.Id == AutomationDefinitionIds.CelTransform
                )
            )
        );
        var transform = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node => node.IsCelTransform);
        await DiscloseAsync(page, transform.Id);
        page.Find("[data-automation-field='binding-value'] input").Input("Connected words");
        await DiscloseAsync(page, send.Id);
        Mode("Connected").Click();
        page.Find("[data-automation-source-picker-open]").Click();
        page.FindAll("[role='radio']")
            .Single(option =>
                option.TextContent.Contains("CEL Transform", StringComparison.Ordinal)
            )
            .Click();
        page.Find("[data-automation-source-picker-complete]").Click();
        await SaveAndReopenAsync();
        var canvas = page.FindComponent<AutomationFlowCanvas>();
        var edge = canvas.Instance.Edges.Single(edge => edge.Kind == AutomationEdgeKind.Data);
        edge.SourceNodeId.ShouldBe(transform.Id);
        edge.TargetNodeId.ShouldBe(send.Id);
        canvas
            .Instance.Nodes.Single(node => node.Id == send.Id)
            .Binding(new("message"))
            .Mode.ShouldBe(AutomationInputBindingMode.Connected);
        Mode("Fixed").Click();
        page.Find("textarea[id$='-message']").GetAttribute("value").ShouldBe(FixedMessage);
        page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldContain(edge);
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.Errors.ShouldNotBeEmpty()
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var persisted = await db.AutomationFlowNodes.SingleAsync(node =>
                node.Id == send.Id.Value
            );
            AutomationRuntimeSerialization
                .RestoreInputBindings(persisted.InputBindingsJson)
                .ShouldBeOfType<AutomationInputBindingsRestoreOutcome.Available>()
                .Bindings[new("message")]
                .Mode.ShouldBe(AutomationInputBindingMode.Connected);
        }
        Mode("Connected").Click();
        await SaveAndReopenAsync();
        page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldContain(edge);

        AngleSharp.Dom.IElement Mode(string mode) =>
            page.FindAll(".automation-binding-mode-tabs button")
                .Single(button => button.TextContent == mode);

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
            await DiscloseAsync(page, send.Id);
        }
    }
}
