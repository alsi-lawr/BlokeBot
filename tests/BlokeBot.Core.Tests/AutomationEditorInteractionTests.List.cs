using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task ListParity_CurrentValidationAndConnectedRepairUseTheSameFacts()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var action = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.SendChatAction
        );
        var source = canvas.Instance.Nodes.Single(node =>
            node.Definition.Kind == AutomationNodeKind.Source
        );
        var flow = canvas.Instance.Edges.Single();
        await f.Page.InvokeAsync(() => canvas.Instance.DeleteEdge.InvokeAsync(flow.Id));
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        var input = transform.TransformInputs.Single();
        inspector
            .FindAll($"[data-automation-field='{input.BindingFieldId.Value}'] button")
            .Single(button => button.TextContent == "Connected")
            .Click();
        var output = transform.TransformOutputs.Single();
        ExpressionField(inspector, output.PortId).Input("input1[");
        await f.Page.InvokeAsync(() =>
            f.Page.FindComponent<AutomationEditorHeader>().Instance.Validate.InvokeAsync()
        );
        canvas.Instance.Errors.ShouldContain(error =>
            error.NodeId == action.Id && error.Code == "node-disconnected"
        );
        canvas.Instance.Errors.ShouldContain(error =>
            error.NodeId == transform.Id && error.PortId == output.PortId
        );
        var producerError = canvas.Instance.Errors.First(error =>
            error.NodeId == transform.Id && error.PortId == output.PortId
        );
        ExpressionField(inspector, output.PortId).GetAttribute("aria-invalid").ShouldBe("true");
        inspector.Markup.ShouldContain(producerError.Diagnostic?.Summary ?? producerError.Message);
        var actionStatus = NodeSelector(f.Page, action.Id).TextContent;
        var producerStatus = NodeSelector(f.Page, transform.Id).TextContent;
        actionStatus.ShouldContain("Needs repair");
        producerStatus.ShouldContain("Needs repair");
        f.Page.Find("[data-automation-mode='list']").Click();
        var list = f.Page.FindComponent<AutomationFlowList>();
        string Status(AutomationNodeId id) =>
            list.Find($"[data-automation-list-node='{id.Value}']")
                .Closest("article")!
                .QuerySelector(".automation-list-status")!
                .TextContent;
        Status(action.Id).ShouldContain("Needs repair");
        Status(transform.Id).ShouldContain("Needs repair");
        Status(transform.Id).ShouldNotContain("Available");
        Status(transform.Id)
            .ShouldContain(producerError.Diagnostic?.Summary ?? producerError.Message);
        list.Find($"[data-automation-list-node='{transform.Id.Value}']").Click();
        inspector = f.Page.FindComponent<AutomationNodeInspector>();
        inspector
            .FindAll($"[data-automation-field='{input.BindingFieldId.Value}'] button")
            .Single(button => button.TextContent == "Fixed")
            .Click();
        ExpressionField(inspector, output.PortId).Input("'repaired'");
        await f.Page.InvokeAsync(() =>
            inspector.Instance.Connect.InvokeAsync(FlowConnection(source, action))
        );
        list.Instance.Validated.ShouldBeFalse();
        list.Instance.Errors.ShouldBeEmpty();
        Status(transform.Id).ShouldContain("Draft");
        Status(transform.Id).ShouldNotContain("Ready");
        await f.Page.InvokeAsync(() =>
            f.Page.FindComponent<AutomationEditorHeader>().Instance.Validate.InvokeAsync()
        );
        list.Instance.Errors.ShouldBeEmpty();
        list.Instance.Validated.ShouldBeTrue();
        Status(transform.Id).ShouldNotContain("Needs repair");
        Status(action.Id).ShouldNotContain("Needs repair");
    }

    [Test]
    public async Task ListParity_ExactConnectionsAndRepairHistoryKeepAuthoredTargets()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var source = canvas.Instance.Nodes.Single(node =>
            node.Definition.Kind == AutomationNodeKind.Source
        );
        var action = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.SendChatAction
        );
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        foreach (var identifier in new[] { "first", "second" })
        {
            await f.Page.InvokeAsync(() =>
                inspector.Instance.AddInputFromSource.InvokeAsync(
                    new(
                        source.Id,
                        source.Definition.Outputs.Single(port => port.Id.Value == "argument.name"),
                        transform.Id,
                        "Same name",
                        identifier
                    )
                )
            );
        }
        var first = transform.TransformInputs[^2];
        var second = transform.TransformInputs[^1];
        var firstEdge = canvas.Instance.Edges.Single(edge => edge.TargetPortId == first.PortId);
        await f.Page.InvokeAsync(() =>
            inspector.Instance.Connect.InvokeAsync(
                new(source.Id, new("argument.name"), action.Id, new("message"))
            )
        );
        var original = CanvasDraft(canvas.Instance);
        var order = canvas.Instance.Nodes.Select(node => node.Id).ToArray();
        var labels = canvas.Instance.Edges.ToDictionary(
            edge => edge.Id,
            edge => canvas.Find($"[data-automation-edge='{edge.Id}']").GetAttribute("aria-label")
        );
        f.Page.Find("[data-automation-mode='list']").Click();
        var list = f.Page.FindComponent<AutomationFlowList>();
        foreach (var edge in list.Instance.Edges)
        {
            list.Find($"[data-automation-list-edge='{edge.Id}']")
                .TextContent.ShouldBe(labels[edge.Id]);
        }
        list.Find($"[data-automation-list-edge='{firstEdge.Id}']")
            .TextContent.ShouldContain(first.PortId.Value);
        list.Find($"[data-automation-list-node='{transform.Id.Value}']").Click();
        list.Instance.Nodes.Select(node => node.Id).ShouldBe(order);
        f.Page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldBe(transform.Id);
        f.Page.Find("button[aria-label='Close Inspector']").Click();
        list.Instance.Nodes.Select(node => node.Id).ShouldBe(order);
        f.Page.Find("[data-automation-mode='grid']").Click();
        CanvasDraft(f.Page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(original);
        f.Page.Find("[data-automation-mode='list']").Click();
        list = f.Page.FindComponent<AutomationFlowList>();
        list.Find($"[data-automation-list-node='{transform.Id.Value}']").Click();
        inspector = f.Page.FindComponent<AutomationNodeInspector>();
        inspector.Find($"[id='automation-declaration-{first.PortId.Value}-type']").Change("Number");
        list.Find($"[data-automation-list-edge='{firstEdge.Id}']")
            .TextContent.ShouldContain("Needs repair");
        inspector
            .Find(
                $"[data-automation-field='{first.BindingFieldId.Value}'] [data-automation-source-picker-open]"
            )
            .Click();
        ChooseSourceRadio(
                inspector,
                source,
                source.Definition.Outputs.Single(port => port.Id.Value == "argument.seconds")
            )
            .Click();
        inspector.Find("[data-automation-source-picker-complete]").Click();
        var repaired = list.Instance.Edges.Single(edge => edge.Id == firstEdge.Id);
        repaired.SourcePortId.Value.ShouldBe("argument.seconds");
        repaired.TargetPortId.ShouldBe(first.PortId);
        repaired.TargetNodeId.ShouldBe(transform.Id);
        list.Find($"[data-automation-list-edge='{firstEdge.Id}']")
            .TextContent.ShouldNotContain("Needs repair");
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        list.Instance.Edges.Single(edge => edge.Id == firstEdge.Id).ShouldBe(firstEdge);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        list.Instance.Edges.Single(edge => edge.Id == firstEdge.Id).ShouldBe(repaired);
        list.Instance.Nodes.Select(node => node.Id).ShouldBe(order);
        list.Find($"[data-automation-list-node='{transform.Id.Value}']").Click();
        inspector = f.Page.FindComponent<AutomationNodeInspector>();
        CelNameField(inspector, second.PortId)
            .Closest(".automation-transform-declaration")!
            .QuerySelector("button[aria-label^='Remove input']")!
            .Click();
        var missingEdge = list.Instance.Edges.Single(edge => edge.TargetPortId == second.PortId);
        list.Find($"[data-automation-list-edge='{missingEdge.Id}']")
            .TextContent.ShouldContain(second.PortId.Value);
        await f.Page.InvokeAsync(() =>
            f.Page.FindComponent<AutomationEditorHeader>().Instance.Validate.InvokeAsync()
        );
        var missing = list.Instance.Errors.Single(error =>
            error.NodeId == transform.Id && error.Code == "port-missing"
        );
        inspector.Instance.Errors.ShouldContain(missing);
        inspector
            .Find(".automation-node-errors")
            .TextContent.ShouldContain(missing.Diagnostic?.Summary ?? missing.Message);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        list.Instance.Edges.Single(edge => edge.Id == missingEdge.Id).ShouldBe(missingEdge);
        list.Find($"[data-automation-list-edge='{missingEdge.Id}']")
            .TextContent.ShouldNotContain("Unavailable");
    }

    [Test]
    [Arguments(AutomationNodeFailurePolicy.Stop)]
    [Arguments(AutomationNodeFailurePolicy.Continue)]
    public async Task ListParity_ActualCurrentAndHistoricalOutcomesKeepTheirProvenance(
        AutomationNodeFailurePolicy policy
    )
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var canvas = f.Page.FindComponent<AutomationFlowCanvas>();
        var action = canvas.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.SendChatAction
        );
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        ExpressionField(inspector, transform.TransformOutputs.Single().PortId)
            .Input("'pure result'");
        await f.Page.InvokeAsync(() =>
            inspector.Instance.Connect.InvokeAsync(
                new(
                    transform.Id,
                    transform.TransformOutputs.Single().PortId,
                    action.Id,
                    new("message")
                )
            )
        );
        await DiscloseAsync(f.Page, action.Id);
        f.Page.Find("#automation-failure-policy").Change(policy.ToString());
        f.Page.Find("[data-automation-test-flow]").Click();
        f.Page.FindComponent<AutomationScenarioPanel>().Find("textarea").Input("5 preview");
        f.Page.Find(".automation-effect-choice select").Change("Failed");
        f.Page.Find("[data-automation-run-scenario]").HasAttribute("disabled").ShouldBeFalse();
        f.Page.Find("[data-automation-run-scenario]").Click();
        f.Page.WaitForAssertion(
            () =>
                canvas.Instance.SampleOutcomes.ShouldContain(outcome =>
                    outcome.NodeId == action.Id
                ),
            TimeSpan.FromSeconds(20)
        );
        var current = f.Page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id;
        var facts = canvas.Instance.SampleOutcomes.ToArray();
        var actionOutcome = facts.Single(outcome => outcome.NodeId == action.Id);
        actionOutcome.State.ShouldBe(
            policy == AutomationNodeFailurePolicy.Continue
                ? AutomationNodeRunState.ContinuedAfterFailure
                : AutomationNodeRunState.Failed
        );
        facts
            .Single(outcome => outcome.NodeId == transform.Id)
            .State.ShouldBe(AutomationNodeRunState.Succeeded);
        var status = f
            .Page.Find($"[data-automation-node='{action.Id.Value}'] .automation-node-status")
            .TextContent;
        f.Page.Find("[data-automation-mode='list']").Click();
        var list = f.Page.FindComponent<AutomationFlowList>();
        list.Instance.SampleOutcomes.ShouldBe(facts);
        list.Find($"[data-automation-list-node='{action.Id.Value}']")
            .Closest("article")!
            .QuerySelector(".automation-list-status")!
            .TextContent.ShouldContain(status);
        f.Page.FindComponent<AutomationRunDrawer>().Instance.Kind.ShouldBe("sample");
        await f.Page.InvokeAsync(() =>
            f
                .Page.FindComponent<AutomationTracePanel>()
                .Instance.SelectRun.InvokeAsync(Guid.NewGuid().ToString())
        );
        list.Instance.SampleOutcomes.ShouldBeEmpty();
        f.Page.FindComponent<AutomationRunDrawer>().Instance.Kind.ShouldBe("history");
        await f.Page.InvokeAsync(() =>
            f
                .Page.FindComponent<AutomationTracePanel>()
                .Instance.SelectRun.InvokeAsync(current.Value.ToString())
        );
        list.Instance.SampleOutcomes.ShouldBe(facts);
        f.Page.Find("#automation-flow-name").Input("Edited after run");
        list.Instance.SampleOutcomes.ShouldAllBe(outcome =>
            outcome.ResolvedInputs.IsDefaultOrEmpty && outcome.VirtualTimeUtc == default
        );
        list.Instance.Validated.ShouldBeFalse();
    }
}
