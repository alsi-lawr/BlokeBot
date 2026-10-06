using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task ContextualMenu_DeleteSetAndIncidentEdges_IsOneExactUndoableEdit()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var canvas = fixture.Page.FindComponent<AutomationFlowCanvas>();
        var original = CanvasDraft(canvas.Instance);
        var ids = canvas.Instance.Nodes.Select(node => node.Id.Value).ToArray();

        var menu = (
            await fixture.Page.Instance.PrepareEditorMenuAsync(ids, null)
        ).ShouldNotBeNull();
        menu.Undo.ShouldBeFalse();
        menu.Redo.ShouldBeFalse();
        CanvasDraft(fixture.Page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(original);
        await fixture.Page.Instance.ApplyEditorMenuAsync(
            AutomationEditorMenuAction.Delete,
            menu.Invocation
        );
        fixture.Page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.ShouldBeEmpty();
        fixture.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBeEmpty();

        var undo = (await fixture.Page.Instance.PrepareEditorMenuAsync([], null)).ShouldNotBeNull();
        undo.Undo.ShouldBeTrue();
        undo.Delete.ShouldBeFalse();
        await fixture.Page.Instance.ApplyEditorMenuAsync(
            AutomationEditorMenuAction.Undo,
            undo.Invocation
        );
        CanvasDraft(fixture.Page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(original);
        var redo = (await fixture.Page.Instance.PrepareEditorMenuAsync([], null)).ShouldNotBeNull();
        redo.Undo.ShouldBeFalse();
        redo.Redo.ShouldBeTrue();
        await fixture.Page.Instance.ApplyEditorMenuAsync(
            AutomationEditorMenuAction.Redo,
            redo.Invocation
        );
        fixture.Page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.ShouldBeEmpty();
    }

    [Test]
    public async Task ContextualMenu_RejectsAnInvocationAfterSupportedMovementOrSelectionChange()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var canvas = fixture.Page.FindComponent<AutomationFlowCanvas>();
        var ids = canvas.Instance.Nodes.Select(node => node.Id.Value).ToArray();
        var captured = (
            await fixture.Page.Instance.PrepareEditorMenuAsync(ids, null)
        ).ShouldNotBeNull();
        fixture
            .Page.Find("[data-automation-node-select]")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        var moved = CanvasDraft(fixture.Page.FindComponent<AutomationFlowCanvas>().Instance);

        await fixture.Page.Instance.ApplyEditorMenuAsync(
            AutomationEditorMenuAction.Delete,
            captured.Invocation
        );
        CanvasDraft(fixture.Page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(moved);
        var current = (
            await fixture.Page.Instance.PrepareEditorMenuAsync(ids, null)
        ).ShouldNotBeNull();
        await fixture.Page.InvokeAsync(() =>
            fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.SetSelectionFromCanvasAsync([ids[0]], null)
        );
        await fixture.Page.Instance.ApplyEditorMenuAsync(
            AutomationEditorMenuAction.Delete,
            current.Invocation
        );
        CanvasDraft(fixture.Page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(moved);
        fixture
            .Page.FindComponent<AutomationFlowCanvas>()
            .Instance.SelectedNodeIds.Select(id => id.Value)
            .ShouldBe([ids[0]]);
    }
}
