using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task SubflowMetadata_IndependentBoundaryEditsPreserveIdentityAndMirroredHistoryThroughPublication()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync();
        var first = await PublishBoundaryFixtureAsync(f);
        await OpenBoundaryFixtureAsync(f.Page, first.SubflowId);
        var before = CanvasDraft(f.Page.FindComponent<AutomationFlowCanvas>().Instance);
        var control = f.Page.FindComponent<AutomationBoundaryPorts>();
        control.Find("[data-automation-port-label=message]").Change("Readable message");
        control
            .Find("[data-automation-port-description=message]")
            .Change("An independently edited description.");
        var beforeEdges = first.Graph.Edges;
        var contract = first.Interface with
        {
            Inputs =
            [
                first.Interface.Inputs[0] with
                {
                    Name = "Readable message",
                    Description = "An independently edited description.",
                },
            ],
        };
        AssertBoundaryContracts(f.Page, contract);
        f.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(beforeEdges);
        AutomationSubflowDefinitions.Compatible(first.Interface, contract).ShouldBeTrue();
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        f.Page.FindComponent<AutomationBoundaryPorts>()
            .Instance.Node.Subflow!.Interface.Inputs[0]
            .Name.ShouldBe("Readable message");
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(f.Page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(before);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        f.Page.Find("[data-automation-save-flow]").Click();
        f.Page.WaitForAssertion(() =>
            f.Page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
        f.Page.Find("[data-automation-publish-subflow]").Click();
        f.Page.WaitForAssertion(() =>
            f.Page.FindAll("[data-automation-publish-subflow]").ShouldBeEmpty()
        );
        var service = f.Context.Services.GetRequiredService<AutomationSubflowService>();
        var saved = (
            await service.LoadCurrentAsync(
                first.Graph.HostId,
                first.SubflowId,
                CancellationToken.None
            )
        ).ShouldNotBeNull();
        saved.Interface.Inputs.ShouldBe(contract.Inputs);
        saved.Graph.Edges.ShouldBe(beforeEdges);
        f.Page.Find("[aria-label='Close authoring panel']").Click();
        var other = await PublishNodeTargetAsync(f, "Other target");
        await OpenBoundaryFixtureAsync(f.Page, other.SubflowId);
        await OpenBoundaryFixtureAsync(f.Page, first.SubflowId);
        AssertBoundaryContracts(f.Page, contract);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubflowMetadata_PublishedCallerPortNavigationAndExplicitRepairKeepSavedState(
        bool nested
    )
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync();
        var target = await PublishBoundaryFixtureAsync(f);
        var page = f.Page;
        AutomationSubflowRevision? parent = null;
        if (nested)
        {
            parent = await PublishNodeTargetAsync(f, "Nested caller");
            await OpenBoundaryFixtureAsync(page, parent.SubflowId);
        }
        var call = await AddInspectorCallAsync(page);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(target.SubflowId)
        );
        call.SetValue(new("message"), "Retain this fixed input");
        var canvas = page.FindComponent<AutomationFlowCanvas>().Instance;
        var origin = canvas.Nodes.Single(n =>
            nested
                ? n.Definition.Id.Value == AutomationSubflowDefinitions.Entry
                : n.Definition.Kind == AutomationNodeKind.Source
        );
        if (nested)
        {
            foreach (var edge in canvas.Edges.ToArray())
            {
                await page.InvokeAsync(() =>
                    page.FindComponent<AutomationFlowCanvas>()
                        .Instance.DeleteEdge.InvokeAsync(edge.Id)
                );
            }
        }
        await DiscloseAsync(page, call.Id);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.Connect.InvokeAsync(
                    new(origin.Id, new(nested ? "complete" : "flow"), call.Id, new("flow"))
                )
        );
        if (nested)
        {
            var exit = canvas.Nodes.Single(n =>
                n.Definition.Id.Value == AutomationSubflowDefinitions.Exit
            );
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationFlowCanvas>()
                    .Instance.Connect.InvokeAsync(
                        new(call.Id, new("complete"), exit.Id, new("flow"))
                    )
            );
        }
        page.Find("[data-automation-save-flow]").Click();
        if (nested)
        {
            page.WaitForAssertion(() =>
                page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
            );
            page.Find("[data-automation-publish-subflow]").Click();
            page.WaitForAssertion(() =>
                page.FindAll("[data-automation-publish-subflow]").ShouldBeEmpty()
            );
        }
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
        );
        var callerDraft = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(n => n.Id == call.Id)
            .Draft();
        if (nested)
        {
            page.Find("[aria-label='Close authoring panel']").Click();
        }
        await OpenBoundaryFixtureAsync(page, target.SubflowId);
        var declaration = page.FindComponent<AutomationBoundaryPorts>();
        declaration.FindAll("button").Single(b => b.TextContent.Trim() == "Add input").Click();
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
        page.FindAll("[data-automation-caller-port]").ShouldBeEmpty();
        page.Find("[data-automation-publish-subflow]").Click();
        var selector =
            $"[data-automation-caller='{call.Id.Value}'][data-automation-caller-port='Input:input-1']";
        page.WaitForAssertion(() => page.Find(selector).ShouldNotBeNull());
        page.Find(selector).Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldBe(call.Id)
        );
        var loaded = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(n => n.Id == call.Id);
        loaded.Subflow!.Interface.Inputs[0].ValueType.ShouldBe(AutomationPortValueType.Text);
        loaded
            .SubflowFixedValue(new("message"))
            .ShouldBe(new AutomationValue.Text("Retain this fixed input"));
        var before = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        var edgesBefore = page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ToArray();
        page.Find("[data-automation-update-call-interface]").Click();
        page.WaitForAssertion(() => loaded.Subflow!.Interface.Inputs.Length.ShouldBe(2));
        loaded.Id.ShouldBe(call.Id);
        loaded.Position.ShouldBe(call.Position);
        loaded.DisplayAlias.ShouldBe(call.DisplayAlias);
        loaded
            .SubflowFixedValue(new("message"))
            .ShouldBe(new AutomationValue.Text("Retain this fixed input"));
        page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(edgesBefore);
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(before);
        await page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        page.Find("#automation-flow-name").Input("Unsaved repair");
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>()
                .Instance.SelectSubflow.InvokeAsync(target.SubflowId)
        );
        var dirty = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        await page.InvokeAsync(
            page.FindComponent<AutomationDirtyDialog>().Instance.Cancel.InvokeAsync
        );
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(dirty);
        var service = f.Context.Services.GetRequiredService<AutomationSubflowService>();
        if (nested)
        {
            var saved = (
                await service.LoadCurrentAsync(
                    target.Graph.HostId,
                    parent!.SubflowId,
                    CancellationToken.None
                )
            ).ShouldNotBeNull();
            System
                .Text.Json.JsonElement.DeepEquals(
                    saved.Graph.Nodes.Single(n => n.Id == call.Id).Definition.Configuration,
                    callerDraft.Definition.Configuration
                )
                .ShouldBeTrue();
        }
        else
        {
            await using var db = await f.Database.CreateDbContextAsync();
            var row = await db.AutomationFlowNodes.SingleAsync(n => n.Id == call.Id.Value);
            AutomationSubflowDefinitions
                .TryRead(
                    new(
                        row.DefinitionId,
                        row.DefinitionSchemaVersion,
                        System.Text.Json.JsonDocument.Parse(row.ConfigurationJson).RootElement
                    ),
                    out var saved
                )
                .ShouldBeTrue();
            saved.Interface.Inputs[0].ValueType.ShouldBe(AutomationPortValueType.Text);
        }
    }
}
