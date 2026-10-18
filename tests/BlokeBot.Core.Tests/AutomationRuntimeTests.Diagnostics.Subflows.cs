using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.Overlays;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Diagnostic_NestedLeafRevisionAndCallerSurviveLiveScenarioUnwind(
        bool continues
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        var graph = await DiagnosticNestedFlow(fixture, continues);
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        var live = await ReadTraceAsync(fixture, new(run.Value));
        fixture.Chat.Messages.ShouldBe(continues ? ["after"] : []);
        writes.Armed = true;
        var simulation = await fixture.Scenarios.RunAsync(
            graph.Caller,
            fixture.Scenarios.CreateDefaultFixture(graph.Caller, graph.Caller.Nodes[0].Id),
            CancellationToken.None
        );
        var testId = continues
            ? simulation.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>().TraceId
            : simulation.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>().TraceId;
        writes.Writes.ShouldBe(0);
        writes.Armed = false;
        foreach (var trace in new[] { live, await ReadTraceAsync(fixture, testId) })
        {
            var caller = trace
                .Events.Single(item =>
                    item.Event.Node?.Id == graph.Caller.Nodes[1].Id
                    && item.Event.Kind == AutomationTraceEventKind.Result
                )
                .Event;
            caller.Outcome.ShouldBe(
                continues
                    ? AutomationTraceOutcome.ContinuedAfterFailure
                    : AutomationTraceOutcome.Failed
            );
            var diagnostic = caller.Diagnostic.ShouldNotBeNull();
            diagnostic.Origin.ShouldNotBeNull().NodeId.ShouldBe(graph.Leaf.Id);
            diagnostic.Origin.Invocation.ShouldNotBeNull().SubflowId.ShouldBe(graph.Inner.Id.Value);
            diagnostic.Origin.Invocation.RevisionId.ShouldBe(graph.Revision.Id.Value);
            _ = diagnostic.Origin.Invocation.ParentId.ShouldNotBeNull();
            diagnostic
                .Member.ShouldBeOfType<AutomationDiagnosticMember.Output>()
                .PortId.ShouldBe(new("value"));
            diagnostic.Span.ShouldBe(
                new AutomationExpressionSpan(2, "decimal('private-nested-number')".Length)
            );
            diagnostic.Affected.ShouldNotBeNull().NodeId.ShouldBe(graph.Caller.Nodes[1].Id);
            trace
                .Events.Where(item => item.Event.Node?.Id == graph.Leaf.Id)
                .ShouldNotContain(item => item.Event.Outcome == AutomationTraceOutcome.Succeeded);
            JsonSerializer.Serialize(trace).ShouldNotContain("private-");
        }
    }

    [Test]
    [Arguments("matching")]
    [Arguments("matching-after-save")]
    [Arguments("changed")]
    [Arguments("deleted")]
    [Arguments("changed-during-guard")]
    public async Task Diagnostic_NestedSourceActionUsesCurrentMatchingRevisionAndRevalidatesAfterDirtyGuard(
        string state
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var graph = await DiagnosticNestedFlow(fixture, false);
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        if (state == "changed")
        {
            _ = await Publish(Subflows(fixture), graph.Inner with { Description = "New revision" });
        }
        if (state == "deleted")
        {
            _ = await Publish(
                Subflows(fixture),
                Subflow(fixture.HostId) with
                {
                    Id = graph.Outer.SubflowId,
                }
            );
            (
                await Subflows(fixture)
                    .RemoveRevisionAsync(
                        new(fixture.HostId),
                        graph.Revision.Id,
                        CancellationToken.None
                    )
            ).ShouldBe(AutomationSubflowRemovalOutcome.Removed);
        }
        using var ui = UiTestContextFactory.Create(
            (SqliteBlokeBotDbFactory)fixture.Database,
            fixture.HostId
        );
        _ = ui.Services.AddSingleton<IPublicChatMessageSender>(fixture.Chat);
        _ = ui.Services.AddSingleton<IOverlayCueAdmissionService>(new NoOverlayCues());
        _ = ui.Services.AddSingleton<TimeProvider>(fixture.Clock);
        _ = ui.Services.AddBlokeBotAutomations();
        _ = ui.Services.AddSingleton(fixture.Scenarios);
        var page = ui.Render<AutomationEditorPage>();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.Count.ShouldBe(graph.Caller.Nodes.Length)
        );
        page.Find(".automation-authoring-openers button:not([data-automation-extract-selection])")
            .Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationTracePanel>()
                .Instance.Snapshot?.Id.ShouldBe(new AutomationTraceId(run.Value))
        );
        var panel = page.FindComponent<AutomationTracePanel>();
        var callerFailure = panel.Instance.Snapshot!.Events.Single(item =>
            item.Event.Node?.Id == graph.Caller.Nodes[1].Id
            && item.Event.Kind == AutomationTraceEventKind.Result
        );
        await page.InvokeAsync(() => panel.Instance.Select.InvokeAsync(callerFailure.Sequence));
        panel
            .Instance.Selected!.Entry.Event.Diagnostic.ShouldNotBeNull()
            .Origin.ShouldNotBeNull()
            .Invocation.ShouldNotBeNull()
            .RevisionId.ShouldBe(graph.Revision.Id.Value);
        panel.Instance.SourceAvailable.ShouldBe(
            state is "matching" or "matching-after-save" or "changed-during-guard"
        );
        if (state is "changed" or "deleted")
        {
            panel.FindAll("[data-automation-diagnostic-source]").ShouldBeEmpty();
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.ShouldContain(node => node.Id == graph.Caller.Nodes[1].Id);
            return;
        }
        if (state == "changed-during-guard")
        {
            page.Find("#automation-flow-name").Input("Dirty caller");
            await page.InvokeAsync(panel.Instance.ShowSource.InvokeAsync);
            page.FindComponent<AutomationDirtyDialog>().Instance.Open.ShouldBeTrue();
            _ = await Publish(
                Subflows(fixture),
                graph.Inner with
                {
                    Description = "Changed while deciding",
                }
            );
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationDirtyDialog>().Instance.Discard.InvokeAsync()
            );
            page.WaitForAssertion(() =>
                page.FindComponent<AutomationTracePanel>().Instance.SourceAvailable.ShouldBeFalse()
            );
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.ShouldContain(node => node.Id == graph.Caller.Nodes[1].Id);
            return;
        }
        if (state == "matching-after-save")
        {
            page.Find("#automation-flow-name").Input("Saved before source navigation");
            await page.InvokeAsync(panel.Instance.ShowSource.InvokeAsync);
            page.FindComponent<AutomationDirtyDialog>().Instance.Open.ShouldBeTrue();
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationDirtyDialog>().Instance.Save.InvokeAsync()
            );
        }
        else
        {
            await page.InvokeAsync(panel.Instance.ShowSource.InvokeAsync);
        }
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldBe(graph.Leaf.Id)
        );
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.ShouldContain(node => node.Id == graph.Leaf.Id);
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.ShouldNotContain(node => node.Id == graph.Caller.Nodes[1].Id);
        ui.JSInterop.Invocations.ShouldContain(call =>
            call.Identifier == "focusInspector" && call.Arguments.Contains("value")
        );
    }

    [Test]
    public async Task Diagnostic_InputFailureKeepsAuthoredFieldAndCurrentFocusAcrossLiveAndScenario()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var predicate = Node(
            "condition",
            """{"predicate":false}""",
            bindings: Bindings(
                "predicate",
                AutomationInputBindingMode.Expression,
                LanguageExpression("decimal(arguments[0]) > 0")
            )
        );
        var draft = Draft(fixture.HostId, [source, predicate], [Edge(source, "flow", predicate)]);
        var saved = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        draft = draft with { Id = saved.FlowId };
        var context = CelTimerContext(fixture.HostId, ["private-invalid"]);
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        var simulation = (
            await fixture.Scenarios.RunAsync(
                draft,
                fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
                {
                    Context = context,
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
        foreach (
            var trace in new[]
            {
                await ReadTraceAsync(fixture, new(run.Value)),
                await ReadTraceAsync(fixture, simulation.TraceId),
            }
        )
        {
            var failure = trace
                .Events.Single(item =>
                    item.Event.Node?.Id == predicate.Id
                    && item.Event.Kind == AutomationTraceEventKind.Result
                )
                .Event;
            failure
                .Diagnostic.ShouldNotBeNull()
                .Origin.ShouldNotBeNull()
                .NodeId.ShouldBe(predicate.Id);
            failure
                .Diagnostic.Member.ShouldBeOfType<AutomationDiagnosticMember.Input>()
                .FieldId.ShouldBe(new("predicate"));
            failure.Diagnostic.Category.ShouldBe(AutomationDiagnosticCategory.Evaluation);
            failure.Diagnostic.Span.ShouldBe(
                new AutomationExpressionSpan(0, "decimal(arguments[0]) > 0".Length)
            );
            JsonSerializer.Serialize(trace).ShouldNotContain("private-");
        }
        fixture.Chat.Calls.ShouldBe(0);
        using var ui = UiTestContextFactory.Create(
            (SqliteBlokeBotDbFactory)fixture.Database,
            fixture.HostId
        );
        _ = ui.Services.AddSingleton<IPublicChatMessageSender>(fixture.Chat);
        _ = ui.Services.AddSingleton<IOverlayCueAdmissionService>(new NoOverlayCues());
        _ = ui.Services.AddSingleton<TimeProvider>(fixture.Clock);
        _ = ui.Services.AddBlokeBotAutomations();
        _ = ui.Services.AddSingleton(fixture.Scenarios);
        var page = ui.Render<AutomationEditorPage>();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.ShouldContain(node => node.Id == predicate.Id)
        );
        page.Find(".automation-authoring-openers button:not([data-automation-extract-selection])")
            .Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationTracePanel>().Instance.SourceAvailable.ShouldBeTrue()
        );
        page.Find("[data-automation-diagnostic-source]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationNodeInspector>().Instance.Node!.Id.ShouldBe(predicate.Id)
        );
        ui.JSInterop.Invocations.ShouldContain(call =>
            call.Identifier == "focusInspector" && call.Arguments[0]!.Equals("predicate")
        );
    }

    private static async Task<DiagnosticNestedGraph> DiagnosticNestedFlow(
        RuntimeFixture fixture,
        bool continues
    )
    {
        var inner = Subflow(fixture.HostId);
        var leaf = Node(
            "cel-transform",
            TransformJson(
                [],
                [
                    new(
                        "value",
                        "Value",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "${decimal('private-nested-number')}"
                    ),
                ]
            )
        );
        var consumer = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        inner = inner with
        {
            Graph = inner.Graph with
            {
                Nodes = [inner.Graph.Nodes[0], leaf, consumer, inner.Graph.Nodes[1]],
                Edges =
                [
                    Edge(inner.Graph.Nodes[0], "complete", consumer),
                    Edge(consumer, "complete", inner.Graph.Nodes[1]),
                    Edge(leaf, "value", consumer, "message", AutomationEdgeKind.Data),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), inner);
        var outer = await Publish(Subflows(fixture), Nesting(Subflow(fixture.HostId), revision));
        var caller = Caller(fixture.HostId, outer);
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = continues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var after = Node("send-chat", """{"message":"after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, after],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", after)],
        };
        var saved = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        return new(inner, revision, outer, leaf, caller with { Id = saved.FlowId });
    }

    private sealed record DiagnosticNestedGraph(
        AutomationSubflowDraft Inner,
        AutomationSubflowRevision Revision,
        AutomationSubflowRevision Outer,
        AutomationFlowDraftNode Leaf,
        AutomationFlowDraft Caller
    );
}
