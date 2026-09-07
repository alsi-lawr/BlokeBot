using System.Collections.Immutable;
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
    public async Task Authoring_QueuedBoundaryFieldEditsMergeCurrentFactsAndKeepPortIdentity()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var original = await PublishBoundaryFixtureAsync(fixture);
        await OpenBoundaryFixtureAsync(fixture.Page, original.SubflowId);
        var node = fixture
            .Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id.Value == AutomationSubflowDefinitions.Entry
            );
        var catalog = fixture.Context.Services.GetRequiredService<AutomationCatalogService>();
        var contract = original.Interface with
        {
            Inputs = [BoundaryPort("message"), BoundaryPort("second"), BoundaryPort("third")],
        };
        void Replace(AutomationSubflowInterface value) =>
            node.ReplaceSubflowDefinition(
                AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, value),
                catalog
            );
        Replace(contract);
        var control = fixture.Context.Render<AutomationBoundaryPorts>(parameters =>
            parameters
                .Add(value => value.Node, node)
                .Add(
                    value => value.Changed,
                    (AutomationBoundaryPortEdit edit) =>
                        Replace(node.Subflow!.Interface with { Inputs = edit.Ports })
                )
        );
        Replace(
            contract with
            {
                Inputs = contract.Inputs.SetItem(
                    0,
                    contract.Inputs[0] with
                    {
                        Nullability = AutomationPortNullability.Nullable,
                    }
                ),
            }
        );
        control.Find("select").Change("Number");
        node.Subflow!.Interface.Inputs[0].ValueType.ShouldBe(AutomationPortValueType.Number);
        node.Subflow.Interface.Inputs[0].Nullability.ShouldBe(AutomationPortNullability.Nullable);
        var beforeRemoval = node.Subflow.Interface;
        var queuedSecondType = control.FindAll("select")[1];
        Replace(beforeRemoval with { Inputs = beforeRemoval.Inputs.RemoveAt(0) });
        queuedSecondType.Change("Number");
        node.Subflow.Interface.Inputs[0].Id.ShouldBe(new AutomationPortId("second"));
        node.Subflow.Interface.Inputs[0].ValueType.ShouldBe(AutomationPortValueType.Number);
        node.Subflow.Interface.Inputs[1].ShouldBe(beforeRemoval.Inputs[2]);
        var removedType = control.Find("select");
        var beforeRejected = node.Subflow.Interface with
        {
            Inputs = node.Subflow.Interface.Inputs.RemoveAt(0),
        };
        Replace(beforeRejected);
        removedType.Change("Boolean");
        node.Subflow.Interface.Inputs.ShouldBe(beforeRejected.Inputs);
        node.Subflow.Interface.Outputs.ShouldBe(beforeRejected.Outputs);
        _ = control.Find("[role=alert]").ShouldNotBeNull();
    }

    [Test]
    public async Task Authoring_BoundaryHalfEditsPreserveReturnBindingsAndMirrorOneUndoableContract()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var original = await PublishBoundaryFixtureAsync(fixture);
        var page = fixture.Page;
        await OpenBoundaryFixtureAsync(page, original.SubflowId);
        var canvas = page.FindComponent<AutomationFlowCanvas>().Instance;
        var before = CanvasDraft(canvas);
        var entry = canvas.Nodes.Single(node =>
            node.Definition.Id.Value == AutomationSubflowDefinitions.Entry
        );
        var exit = canvas.Nodes.Single(node =>
            node.Definition.Id.Value == AutomationSubflowDefinitions.Exit
        );
        var exitBefore = exit.Draft();
        var edges = canvas.Edges.ToArray();
        var added = original.Interface.Inputs.Add(BoundaryPort("extra"));
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationBoundaryPorts>()
                .Instance.Changed.InvokeAsync(new(entry, added))
        );
        AssertBoundaryContracts(page, original.Interface with { Inputs = added });
        exit.Draft().InputBindings.ShouldBe(exitBefore.InputBindings);
        exit.Position.ShouldBe(exitBefore.Position);
        exit.DisplayAlias.ShouldBe(exitBefore.DisplayAlias);
        exit.FailurePolicy.ShouldBe(exitBefore.FailurePolicy);
        exit.SubflowFixedValue(new("fixed")).ShouldBe(new AutomationValue.Text("1"));
        page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(edges);
        var after = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        page.FindComponent<AutomationBoundaryPorts>().Find("input").Change("extra");
        page.FindComponent<AutomationBoundaryPorts>()
            .Find("input")
            .GetAttribute("value")
            .ShouldBe("message");
        _ = page.FindComponent<AutomationBoundaryPorts>().Find("[role=alert]").ShouldNotBeNull();
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(after);
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(before);
        await page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(after);
        exit = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node => node.Id == exit.Id);
        await DiscloseAsync(page, exit.Id);
        var outputs = original.Interface.Outputs.SetItem(
            0,
            original.Interface.Outputs[0] with
            {
                ValueType = AutomationPortValueType.Number,
            }
        );
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationBoundaryPorts>()
                .Instance.Changed.InvokeAsync(new(exit, outputs))
        );
        AssertBoundaryContracts(page, new(added, outputs));
        exit.SubflowFixedValue(new("fixed")).ShouldBe(new AutomationValue.Text("1"));
        exit.Draft().InputBindings.ShouldBe(exitBefore.InputBindings);
        page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(edges);
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.FindAll("[data-automation-publication-preview]").ShouldBeEmpty()
        );
        page.Find("[aria-label='Close authoring panel']").Click();
        await DiscloseAsync(page, exit.Id);
        page.Find("[data-automation-set-fixed-value]").Click();
        exit.SubflowFixedValue(new("fixed")).ShouldBe(new AutomationValue.Number(1));
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publication-preview]").ShouldNotBeNull()
        );
        page.Find("[data-automation-subflow-review] input").Change("Changed description");
        page.FindAll("[data-automation-publish-subflow]").ShouldBeEmpty();
        page.Find("[data-automation-review-subflow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
        page.Find("[data-automation-publish-subflow]").Click();
        page.WaitForAssertion(() =>
            page.FindAll("[data-automation-publication-preview]").ShouldBeEmpty()
        );
        var service = fixture.Context.Services.GetRequiredService<AutomationSubflowService>();
        var saved = (
            await service.LoadCurrentAsync(
                original.Graph.HostId,
                original.SubflowId,
                CancellationToken.None
            )
        ).ShouldNotBeNull();
        saved.Description.ShouldBe("Changed description");
        saved.Interface.Inputs.ShouldBe(added);
        saved.Interface.Outputs.ShouldBe(outputs);
        foreach (var node in saved.Graph.Nodes)
        {
            AutomationSubflowDefinitions
                .TryRead(node.Definition, out var configuration)
                .ShouldBeTrue();
            configuration.Interface.Inputs.ShouldBe(saved.Interface.Inputs);
            configuration.Interface.Outputs.ShouldBe(saved.Interface.Outputs);
        }
        var persistedExit = saved.Graph.Nodes.Single(node => node.Id == exit.Id);
        persistedExit.InputBindings.ShouldBe(exitBefore.InputBindings);
        saved.Graph.Edges.ShouldBe(edges);
    }

    [Test]
    public async Task Authoring_RemovedReturnValuesAndMissingBoundariesStayInvalidUntilExplicitRepair()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var original = await PublishBoundaryFixtureAsync(fixture);
        var page = fixture.Page;
        await OpenBoundaryFixtureAsync(page, original.SubflowId);
        var canvas = page.FindComponent<AutomationFlowCanvas>().Instance;
        var entry = canvas.Nodes.Single(node =>
            node.Definition.Id.Value == AutomationSubflowDefinitions.Entry
        );
        var exit = canvas.Nodes.Single(node =>
            node.Definition.Id.Value == AutomationSubflowDefinitions.Exit
        );
        await DiscloseAsync(page, exit.Id);
        var before = CanvasDraft(canvas);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationBoundaryPorts>()
                .Instance.Changed.InvokeAsync(new(exit, []))
        );
        exit.UnmatchedSubflowInputs.Select(field => field.Value)
            .ShouldBe(["fixed", "connected", "calculated"], ignoreOrder: true);
        exit.SubflowFixedValue(new("fixed")).ShouldBe(new AutomationValue.Text("1"));
        exit.Binding(new("calculated")).Expression!.Source.ShouldBe("\"expression return\"");
        _ = canvas.Edges.Where(edge => edge.Kind == AutomationEdgeKind.Data).ShouldHaveSingleItem();
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-subflow-review]").ShouldNotBeNull()
        );
        page.FindAll("[data-automation-publish-subflow]").ShouldBeEmpty();
        page.Find("[aria-label='Close authoring panel']").Click();
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(before);
        await DiscloseAsync(page, exit.Id);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.DeleteNode.InvokeAsync(exit.Id)
        );
        await DiscloseAsync(page, entry.Id);
        var missing = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        page.FindComponent<AutomationBoundaryPorts>().Find("input").Change("renamed");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(missing);
        page.FindComponent<AutomationBoundaryPorts>()
            .Find("input")
            .GetAttribute("value")
            .ShouldBe("message");
        _ = page.FindComponent<AutomationBoundaryPorts>().Find("[role=alert]").ShouldNotBeNull();
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(before);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Authoring_BoundaryNewLoadAndFailedLoadPreserveModeAndDirtyDocument(
        bool list,
        bool focus
    )
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var original = await PublishBoundaryFixtureAsync(fixture);
        var page = fixture.Page;
        if (focus)
        {
            page.Find("[aria-label=Focus]").Click();
        }
        if (list)
        {
            await page.InvokeAsync(
                page.FindComponent<AutomationEditorHeader>().Instance.SelectList.InvokeAsync
            );
        }
        page.Find("[data-automation-new-subflow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>()
                .Instance.Node!.Definition.Id.Value.ShouldBe(AutomationSubflowDefinitions.Entry)
        );
        page.FindComponent<AutomationNodeInspector>().Instance.MobileOpen.ShouldBeTrue();
        page.FindComponent<AutomationEditorHeader>().Instance.ListSelected.ShouldBe(list);
        page.FindComponent<AutomationEditorHeader>().Instance.FocusMode.ShouldBe(focus);
        page.Find("#automation-flow-name").Input("Keep my draft");
        var selected = page.FindComponent<AutomationNodeInspector>().Instance.Node;
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>()
                .Instance.SelectSubflow.InvokeAsync(new(Guid.NewGuid()))
        );
        page.Find(".automation-dirty-dialog .automation-danger-button").Click();
        page.WaitForAssertion(() => page.FindAll(".automation-dirty-dialog").ShouldBeEmpty());
        page.Find("#automation-flow-name").GetAttribute("value").ShouldBe("Keep my draft");
        page.FindComponent<AutomationNodeInspector>().Instance.Node.ShouldBeSameAs(selected);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>()
                .Instance.SelectSubflow.InvokeAsync(original.SubflowId)
        );
        _ = page.Find(".automation-dirty-dialog").ShouldNotBeNull();
        page.Find(".automation-dirty-dialog .automation-danger-button").Click();
        page.WaitForAssertion(() =>
            page.Find("#automation-flow-name").GetAttribute("value").ShouldBe(original.Graph.Name)
        );
        page.FindComponent<AutomationNodeInspector>()
            .Instance.Node!.Id.ShouldBe(original.Graph.Nodes[0].Id);
        page.FindComponent<AutomationEditorHeader>().Instance.ListSelected.ShouldBe(list);
        page.FindComponent<AutomationEditorHeader>().Instance.FocusMode.ShouldBe(focus);
    }

    [Test]
    public async Task Authoring_BoundaryEditsRejectOlderReviewAndRetainDescriptionPublication()
    {
        var gate = new CallReadGate();
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var original = await PublishBoundaryFixtureAsync(fixture);
        var page = fixture.Page;
        await OpenBoundaryFixtureAsync(page, original.SubflowId);
        var entry = page.FindComponent<AutomationNodeInspector>().Instance.Node!;
        gate.Arm();
        var reviewing = page.InvokeAsync(
            page.FindComponent<AutomationEditorHeader>().Instance.Save.InvokeAsync
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationBoundaryPorts>()
                    .Instance.Changed.InvokeAsync(
                        new(entry, original.Interface.Inputs.Add(BoundaryPort("later")))
                    )
            );
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await reviewing;
        page.FindAll("[data-automation-publication-preview]").ShouldBeEmpty();
        page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldBe(entry.Id);
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publication-preview]").ShouldNotBeNull()
        );
        page.Find("[data-automation-subflow-review] input").Change("New description");
        page.FindAll("[data-automation-publish-subflow]").ShouldBeEmpty();
        page.Find("[data-automation-review-subflow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
    }

    private static AutomationPortMetadata BoundaryPort(string name) =>
        new(new(name), name, name, AutomationPortValueType.Text);

    private static async Task OpenBoundaryFixtureAsync(
        IRenderedComponent<AutomationEditorPage> page,
        AutomationSubflowId id
    )
    {
        await BrowseLibraryAsync(page, AutomationLibraryKind.Subflows);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>().Instance.SelectSubflow.InvokeAsync(id)
        );
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>()
                .Instance.Node!.Definition.Id.Value.ShouldBe(AutomationSubflowDefinitions.Entry)
        );
    }

    private static void AssertBoundaryContracts(
        IRenderedComponent<AutomationEditorPage> page,
        AutomationSubflowInterface expected
    )
    {
        var nodes = page.FindComponent<AutomationFlowCanvas>().Instance.Nodes;
        foreach (var boundary in nodes)
        {
            boundary.Subflow!.Interface.Outputs.ShouldBe(expected.Outputs);
        }
        nodes
            .Single(node => node.Definition.Id.Value == AutomationSubflowDefinitions.Entry)
            .Subflow!.Interface.Inputs.ShouldBe(expected.Inputs);
        nodes
            .Single(node => node.Definition.Id.Value == AutomationSubflowDefinitions.Exit)
            .Subflow!.Interface.Inputs.ShouldBe(expected.Inputs);
    }

    private static async Task<AutomationSubflowRevision> PublishBoundaryFixtureAsync(
        AutomationEditorPageFixture fixture
    )
    {
        var catalog = fixture.Context.Services.GetRequiredService<AutomationCatalogService>();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var host = await db.Hosts.Select(value => value.Id).SingleAsync();
        var contract = new AutomationSubflowInterface(
            [BoundaryPort("message")],
            [BoundaryPort("fixed"), BoundaryPort("connected"), BoundaryPort("calculated")]
        );
        var entry = AutomationEditorNode
            .FromDefinition(
                AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, contract),
                catalog,
                new(new(75), new(100)),
                "Authored Entry"
            )
            .Draft();
        var exit = AutomationEditorNode
            .FromDefinition(
                AutomationSubflowDefinitions.Boundary(
                    AutomationSubflowDefinitions.Exit,
                    contract,
                    ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
                        new("fixed"),
                        new AutomationValue.Text("1")
                    )
                ),
                catalog,
                new(new(370), new(220)),
                "Authored Exit"
            )
            .Draft();
        exit = exit with
        {
            FailurePolicy = AutomationNodeFailurePolicy.Continue,
            InputBindings = exit
                .InputBindings.SetItem(
                    new("connected"),
                    new(AutomationInputBindingMode.Connected, null)
                )
                .SetItem(
                    new("calculated"),
                    new(
                        AutomationInputBindingMode.Expression,
                        new(AutomationExpressionLanguage.CurrentVersion, "\"expression return\"")
                    )
                ),
        };
        var draft = new AutomationSubflowDraft(
            new(Guid.NewGuid()),
            "Boundary fixture",
            contract,
            new(
                null,
                new(host),
                "Boundary study",
                1,
                false,
                [entry, exit],
                [
                    new(
                        Guid.NewGuid(),
                        AutomationEdgeKind.Flow,
                        entry.Id,
                        new("complete"),
                        exit.Id,
                        new("flow")
                    ),
                    new(
                        Guid.NewGuid(),
                        AutomationEdgeKind.Data,
                        entry.Id,
                        new("message"),
                        exit.Id,
                        new("connected")
                    ),
                ]
            )
        );
        return (
            await fixture
                .Context.Services.GetRequiredService<AutomationSubflowService>()
                .PublishAsync(draft, CancellationToken.None)
        )
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Published>()
            .Revision;
    }
}
