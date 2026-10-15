using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    [Arguments(AutomationEdgeKind.Flow)]
    [Arguments(AutomationEdgeKind.Data)]
    public async Task ConnectionKeyboard_DeletePreservesEndpointsAndIsOneExactHistoryEdit(
        AutomationEdgeKind kind
    )
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var source = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.CustomCommandSource
        );
        await f.Page.InvokeAsync(() =>
            f
                .Page.FindComponent<AutomationNodeInspector>()
                .Instance.AddInputFromSource.InvokeAsync(
                    new(
                        source.Id,
                        source.Definition.Outputs.Single(port => port.Id.Value == "argument.name"),
                        transform.Id,
                        "Current name",
                        "source_name"
                    )
                )
        );
        var input = transform.TransformInputs.Last();
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        void Mode(string mode) =>
            inspector
                .Find($"[data-automation-field='{input.BindingFieldId.Value}']")
                .QuerySelectorAll(".automation-binding-mode-tabs button")
                .Single(button => button.TextContent == mode)
                .Click();
        Mode("Fixed");
        inspector
            .Find($"[data-automation-field='{input.BindingFieldId.Value}'] input")
            .Input("retained literal");
        Mode("Expression");
        inspector
            .Find($"[data-automation-field='{input.BindingFieldId.Value}'] textarea")
            .Input("\"retained expression\"");
        Mode("Connected");
        canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var data = canvas.Instance.Edges.Single(edge => edge.TargetNodeId == transform.Id);
        var edge =
            kind == AutomationEdgeKind.Data
                ? data
                : canvas.Instance.Edges.First(candidate => candidate.Kind == kind);
        f.Page.Find("[data-automation-save-flow]").Click();
        f.Page.WaitForAssertion(() =>
            f.Page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
        );
        canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var before = CanvasDraft(canvas.Instance);
        var nodesBefore = JsonDocument.Parse(before).RootElement.GetProperty("Nodes").GetRawText();
        var edgesBefore = canvas.Instance.Edges.ToArray();
        await f.Page.InvokeAsync(() =>
            canvas.Instance.SetSelectionFromCanvasAsync([transform.Id.Value], null)
        );
        await f.Page.InvokeAsync(() => canvas.Instance.SetSelectionFromCanvasAsync([], edge.Id));
        CanvasDraft(canvas.Instance).ShouldBe(before);
        f.Page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse();
        await f.Page.InvokeAsync(() =>
            canvas.Instance.SetSelectionFromCanvasAsync([transform.Id.Value], null)
        );

        await f.Page.InvokeAsync(() =>
            canvas.Instance.DeleteSelectionFromCanvasAsync([transform.Id.Value], edge.Id)
        );

        canvas.Instance.Edges.ShouldBe(edgesBefore.Where(candidate => candidate.Id != edge.Id));
        canvas.Instance.SelectedNodeIds.ShouldBeEmpty();
        canvas.Instance.SelectedEdgeId.ShouldBeNull();
        JsonDocument
            .Parse(CanvasDraft(canvas.Instance))
            .RootElement.GetProperty("Nodes")
            .GetRawText()
            .ShouldBe(nodesBefore);
        CurrentTransform(f.Page, transform.Id)
            .Binding(transform.TransformInputs.Last().BindingFieldId)
            .Mode.ShouldBe(AutomationInputBindingMode.Connected);
        var retained = CurrentTransform(f.Page, transform.Id);
        retained
            .TransformInputs.Last()
            .FixedValue.ShouldBe(new AutomationValue.Text("retained literal"));
        retained
            .Binding(input.BindingFieldId)
            .Expression!.Source.ShouldBe("\"retained expression\"");
        if (kind == AutomationEdgeKind.Data)
        {
            await DiscloseAsync(f.Page, transform.Id);
            f.Page.FindComponent<AutomationNodeInspector>()
                .Find(
                    $"[data-automation-field='{input.BindingFieldId.Value}'] .automation-binding-card strong"
                )
                .TextContent.ShouldNotContain(source.EffectiveName);
        }
        var deleted = CanvasDraft(canvas.Instance);
        f.Page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeTrue();
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(canvas.Instance).ShouldBe(before);
        f.Page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse();
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        CanvasDraft(canvas.Instance).ShouldBe(deleted);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(canvas.Instance).ShouldBe(before);
    }

    [Test]
    public async Task ConnectionNames_FollowExactCurrentPortsAndRetainUnavailableIdentity()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var source = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.CustomCommandSource
        );
        foreach (var name in new[] { "First", "Second" })
        {
            await f.Page.InvokeAsync(() =>
                f
                    .Page.FindComponent<AutomationNodeInspector>()
                    .Instance.AddInputFromSource.InvokeAsync(
                        new(
                            source.Id,
                            source.Definition.Outputs.Single(port =>
                                port.Id.Value == "argument.name"
                            ),
                            transform.Id,
                            name,
                            name.ToLowerInvariant()
                        )
                    )
            );
        }
        var first = transform.TransformInputs[^2];
        var second = transform.TransformInputs[^1];
        var firstEdge = canvas.Instance.Edges.Single(edge => edge.TargetPortId == first.PortId);
        var secondEdge = canvas.Instance.Edges.Single(edge => edge.TargetPortId == second.PortId);
        string Label(Guid id) =>
            canvas.Find($"[data-automation-edge='{id}']").GetAttribute("aria-label")!;
        Label(firstEdge.Id).ShouldContain(source.EffectiveName);
        Label(firstEdge.Id)
            .ShouldContain(
                source.Definition.Outputs.Single(port => port.Id == firstEdge.SourcePortId).Name
            );
        Label(firstEdge.Id).ShouldContain(first.DisplayName);
        Label(secondEdge.Id).ShouldContain(second.DisplayName);
        Label(firstEdge.Id).ShouldNotBe(Label(secondEdge.Id));
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        CelNameField(inspector, first.PortId)
            .Closest(".automation-transform-declaration")!
            .QuerySelector("input")!
            .Input("Second");
        Label(firstEdge.Id).ShouldContain(first.PortId.Value);
        Label(secondEdge.Id).ShouldContain(second.PortId.Value);
        CelNameField(inspector, first.PortId)
            .Closest(".automation-transform-declaration")!
            .QuerySelector("button[aria-label^='Remove input']")!
            .Click();
        Label(firstEdge.Id).ShouldContain("Unavailable");
        Label(firstEdge.Id).ShouldContain(first.PortId.Value);
        Label(secondEdge.Id).ShouldContain(second.DisplayName);
        Label(secondEdge.Id).ShouldNotContain(first.PortId.Value);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        Label(firstEdge.Id).ShouldNotContain("Unavailable");
        Label(firstEdge.Id).ShouldContain(first.PortId.Value);
    }
}
