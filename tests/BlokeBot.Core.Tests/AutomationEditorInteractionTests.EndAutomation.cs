using System.Text.Json;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task EndAutomation_OrdinaryAddWireRejectOutgoingSaveAndReopenRetainEndpoint()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync();
        var page = f.Page;
        page.Find("[data-automation-toolbox-toggle]").Click();
        var definition = page.FindComponent<AutomationToolbox>()
            .Instance.Definitions.Single(item =>
                item.Id == AutomationDefinitionIds.EndAutomationControl
            );
        page.Find("[data-automation-toolbox-search]").Input(definition.Display.Name);
        var row = page.FindComponents<EditorToolboxRow>()
            .Single(item =>
                item.FindComponent<AutomationNodeIcon>().Instance.DefinitionId == definition.Id
            );
        await page.InvokeAsync(row.Instance.Activate.InvokeAsync);
        var canvas = page.FindComponent<AutomationFlowCanvas>();
        var end = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.EndAutomationControl
        );
        var source = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.StreamOnlineSource
        );
        var send = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.SendChatAction
        );
        await page.InvokeAsync(() =>
            canvas.Instance.ConnectFromCanvasAsync(source.Id.Value, "flow", end.Id.Value, "flow")
        );
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.Edges.ShouldContain(edge =>
                    edge.SourceNodeId == source.Id && edge.TargetNodeId == end.Id
                )
        );
        var before = page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ToArray();
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.ConnectFromCanvasAsync(end.Id.Value, "complete", send.Id.Value, "flow")
        );
        page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(before);
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
        );
        var rail = page.FindComponent<AutomationFlowRail>();
        await page.InvokeAsync(() =>
            rail.Instance.Select.InvokeAsync(rail.Instance.Flows.Single())
        );
        var reopened = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node => node.Id == end.Id);
        reopened.Definition.Id.ShouldBe(AutomationDefinitionIds.EndAutomationControl);
        await using var db = await f.Database.CreateDbContextAsync();
        var saved = await db.AutomationFlowNodes.SingleAsync(node => node.Id == end.Id.Value);
        var configuration = JsonSerializer.Deserialize<JsonElement>(saved.ConfigurationJson);
        configuration.ValueKind.ShouldBe(JsonValueKind.Object);
        configuration.EnumerateObject().ShouldBeEmpty();
        (
            await db
                .AutomationFlowEdges.Where(edge => edge.SourceNodeId == end.Id.Value)
                .CountAsync()
        ).ShouldBe(0);
        (
            await db
                .AutomationFlowEdges.Where(edge => edge.TargetNodeId == end.Id.Value)
                .SingleAsync()
        ).SourceNodeId.ShouldBe(source.Id.Value);
    }
}
