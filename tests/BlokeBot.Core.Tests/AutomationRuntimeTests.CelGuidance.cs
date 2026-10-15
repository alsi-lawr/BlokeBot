using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.Overlays;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CelGuidance_PersistedLiveAndScenarioFailuresSelectProducerWithoutInventingCause(
        bool scenario,
        bool numericFailure
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var producer = CelTimerParse(
            numericFailure ? "decimal(args[0])" : "decimal(size(args))",
            numericFailure ? "'private-earlier-output'" : "args[99]"
        );
        var consumer = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var draft = Draft(
            fixture.HostId,
            [source, producer, consumer],
            [
                Edge(source, "flow", consumer),
                Edge(producer, "name", consumer, "message", AutomationEdgeKind.Data),
            ]
        );
        var flow = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        draft = draft with { Id = flow.FlowId };
        var context = CelTimerContext(fixture.HostId, ["private-invalid-number", "private-name"]);
        AutomationTraceId traceId;
        if (scenario)
        {
            writes.Armed = true;
            var result = (
                await fixture.Scenarios.RunAsync(
                    draft,
                    fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
                    {
                        Context = context,
                    },
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
            traceId = result.TraceId;
            writes.Writes.ShouldBe(0);
            writes.Armed = false;
        }
        else
        {
            var run = (
                await fixture.Runtime.DispatchAsync(
                    new(context, new CustomCommandSourceConfiguration(new(7))),
                    CancellationToken.None
                )
            ).RunIds.ShouldHaveSingleItem();
            traceId = new(run.Value);
            await using var db = await fixture.Database.CreateDbContextAsync();
            (
                await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == producer.Id.Value)
            ).OutputJson.ShouldBeNull();
        }
        fixture.Chat.Calls.ShouldBe(0);
        var persisted = await ReadTraceAsync(fixture, traceId);
        var failed = persisted.Events.Single(entry =>
            entry.Event.Node?.Id == producer.Id
            && entry.Event.Kind == AutomationTraceEventKind.Outputs
        );
        failed.Event.Outcome.ShouldBe(AutomationTraceOutcome.Failed);
        failed.Event.Values.ShouldBeEmpty();
        var serialized = JsonSerializer.Serialize(persisted);
        serialized.ShouldNotContain("private-");
        serialized.ShouldNotContain(numericFailure ? "decimal(args[0])" : "args[99]");
        var presentation = AutomationTracePresentation.Create(persisted);
        presentation.RootOutcomes.ShouldNotContain(outcome => outcome.NodeId == producer.Id);
        presentation
            .RootOutcomes.Single(outcome => outcome.NodeId == consumer.Id)
            .State.ShouldBe(AutomationNodeRunState.Failed);

        // Repair the current source before opening the old persisted failure. Advice cannot depend on current CEL or an exception.
        var repaired = CelTimerParse("1", "'repaired'") with
        {
            Id = producer.Id,
        };
        _ = (
            await fixture.Flows.SaveAsync(
                draft with
                {
                    Nodes = draft.Nodes.Replace(producer, repaired),
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        using var ui = UiTestContextFactory.Create(fixture.Database, fixture.HostId);
        _ = ui.Services.AddSingleton<IPublicChatMessageSender>(fixture.Chat);
        _ = ui.Services.AddSingleton<IOverlayCueAdmissionService>(new NoOverlayCues());
        _ = ui.Services.AddSingleton<TimeProvider>(fixture.Clock);
        _ = ui.Services.AddBlokeBotAutomations();
        var page = ui.Render<AutomationEditorPage>();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.Count.ShouldBe(3)
        );
        page.Find(".automation-authoring-openers button:not([data-automation-extract-selection])")
            .Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationTracePanel>().Instance.Snapshot?.Id.ShouldBe(traceId)
        );
        var panel = page.FindComponent<AutomationTracePanel>();
        panel.Instance.Selected!.Entry.Event.Node!.Id.ShouldBe(consumer.Id);
        panel.FindAll(".automation-trace-detail [role=status]").ShouldBeEmpty();
        await page.InvokeAsync(() => panel.Instance.Select.InvokeAsync(failed.Sequence));
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.SelectedNodeIds.ShouldBe(new HashSet<AutomationNodeId> { producer.Id });
        page.FindComponent<AutomationTracePanel>()
            .Instance.Selected!.Entry.Sequence.ShouldBe(failed.Sequence);
        ui.JSInterop.Invocations.ShouldContain(call =>
            call.Identifier == "revealTraceNode"
            && call.Arguments.Contains(producer.Id.Value.ToString())
        );
        var advice = page.Find(".automation-trace-detail [role=status]");
        // The rendered advice is the same generic remediation for numeric and nonnumeric failures, not a diagnosis.
        advice.TextContent.ShouldNotContain("private-");
        advice.TextContent.ShouldNotContain("args[99]");
        var selectedAdvice = advice.TextContent;
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationTracePanel>()
                .Instance.Select.InvokeAsync(
                    persisted
                        .Events.Single(entry =>
                            entry.Event.Kind == AutomationTraceEventKind.Result
                            && entry.Event.Node?.Id == consumer.Id
                        )
                        .Sequence
                )
        );
        page.FindAll(".automation-trace-detail [role=status]").ShouldBeEmpty();
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationTracePanel>().Instance.Select.InvokeAsync(failed.Sequence)
        );
        page.Find(".automation-trace-detail [role=status]").TextContent.ShouldBe(selectedAdvice);
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.SampleOutcomes.ShouldNotContain(outcome => outcome.NodeId == producer.Id);
    }
}
