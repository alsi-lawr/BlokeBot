using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task EndAutomation_SoleEndpointAndLegacyImplicitEndDrainNormally()
    {
        await using var f = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var end = Node("end-automation", "{}");
        var draft = Draft(f.HostId, [source, end], [Edge(source, "flow", end)]);
        _ = (
            await f.Flows.SaveAsync(draft, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var implicitSource = Node("custom-command", """{"custom-command-id":8}""");
        var implicitEnd = Node("send-chat", """{"message":"implicit endpoint"}""");
        _ = (
            await f.Flows.SaveAsync(
                Draft(
                    f.HostId,
                    [implicitSource, implicitEnd],
                    [Edge(implicitSource, "flow", implicitEnd)]
                ),
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var id = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        var trace = await ReadTraceAsync(f, new(id.Value));
        EndResult(trace, end.Id).Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        trace.Events.ShouldNotContain(item =>
            item.Event.Node != null
            && item.Event.Node.Id == end.Id
            && item.Event.Kind == AutomationTraceEventKind.Branch
        );
        trace.Events[^1].Event.Kind.ShouldBe(AutomationTraceEventKind.Terminal);
        trace.Events[^1].Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync();
            run.Status.ShouldBe(AutomationFlowRunStatus.Completed);
            _ = run.CompletedAtUtc.ShouldNotBeNull();
        }
        _ = await f.NewRuntime()
            .DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(8))),
                default
            );
        f.Chat.Messages.ShouldBe(["implicit endpoint"]);
        var saved = (
            await f.Flows.ListAsync(new(f.HostId), default)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        saved
            .Flows.Single(flow => flow.Draft.Nodes.Any(node => node.Id == implicitEnd.Id))
            .Draft.Nodes.Select(node => node.Id.Value)
            .Order()
            .ShouldBe(new[] { implicitSource.Id.Value, implicitEnd.Id.Value }.Order());
    }

    [Test]
    public async Task EndAutomation_PendingSiblingKeepsExactDueAndFrozenWorkAcrossRestart()
    {
        await using var f = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var graph = EndFanout(f.HostId);
        var id = (await f.Flows.SaveAsync(graph, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var due = f.Clock.GetUtcNow().UtcDateTime.AddSeconds(1);
        var runId = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        var end = graph.Nodes[2];
        var delay = graph.Nodes[1];
        var send = graph.Nodes[3];
        var waitingTrace = await ReadTraceAsync(f, new(runId.Value));
        var ended = EndResult(waitingTrace, end.Id);
        ended.Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        var delayed = waitingTrace.Events.Single(item =>
            item.Event.Node != null
            && item.Event.Node.Id == delay.Id
            && item.Event.Kind == AutomationTraceEventKind.Delay
        );
        delayed.Sequence.ShouldBeLessThan(ended.Sequence);
        delayed.Event.DueAtUtc.ShouldBe(new DateTimeOffset(due));
        waitingTrace.Events.ShouldNotContain(item =>
            item.Event.Node != null
            && item.Event.Node.Id == end.Id
            && item.Event.Kind == AutomationTraceEventKind.Branch
        );
        waitingTrace.Events.ShouldNotContain(item =>
            item.Event.Kind == AutomationTraceEventKind.Terminal
        );
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync();
            run.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
            run.CompletedAtUtc.ShouldBeNull();
            var pending = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == send.Id.Value
            );
            pending.Status.ShouldBe(AutomationNodeRunStatus.Pending);
            pending.AvailableAtUtc.ShouldBe(due);
            (
                await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == end.Id.Value)
            ).Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(run.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>()
                .Flow;
            var frozenEnd = frozen.Nodes.Single(node => node.Id == end.Id.Value);
            frozenEnd.DefinitionId.ShouldBe("end-automation");
            frozenEnd.DefinitionSchemaVersion.ShouldBe(1);
        }
        var changedSend = Node("send-chat", """{"message":"edited live graph"}""") with
        {
            Id = send.Id,
        };
        _ = (
            await f.Flows.SaveAsync(
                graph with
                {
                    Id = id,
                    Nodes = graph
                        .Nodes.Replace(send, changedSend)
                        .Replace(
                            delay,
                            Node("delay", """{"duration-milliseconds":5000}""") with
                            {
                                Id = delay.Id,
                            }
                        ),
                },
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        f.Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - 1));
        (await f.NewRuntime().ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        f.Chat.Calls.ShouldBe(0);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (
                await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == send.Id.Value)
            ).AvailableAtUtc.ShouldBe(due);
            (await db.AutomationFlowRuns.SingleAsync()).CompletedAtUtc.ShouldBeNull();
        }
        f.Clock.Advance(TimeSpan.FromTicks(1));
        (await f.NewRuntime().ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        await f.NewRuntime().ResumeDueAsync(default);
        (await f.NewRuntime().ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        f.Chat.Messages.ShouldBe(["sibling-private-value"]);
        var completed = await ReadTraceAsync(f, new(runId.Value));
        EndResult(completed, end.Id).Sequence.ShouldBe(ended.Sequence);
        completed
            .Events.Where(item =>
                item.Event.Node != null
                && item.Event.Node.Id == send.Id
                && item.Event.Kind == AutomationTraceEventKind.Result
            )
            .ShouldHaveSingleItem()
            .Event.TimeUtc.ShouldBe(new DateTimeOffset(due));
        completed
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.Terminal)
            .ShouldHaveSingleItem()
            .Event.TimeUtc.ShouldBe(new DateTimeOffset(due));
    }

    [Test]
    public async Task EndAutomation_ScenarioDrainsVirtualSiblingWithOnlyValueFreeTraceWrites()
    {
        var guard = new ScenarioWriteGuard();
        await using var f = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [guard],
            migrateSchema: true
        );
        var graph = EndFanout(f.HostId);
        var flow = (await f.Flows.SaveAsync(graph, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        graph = graph with { Id = flow };
        var scenario = f.Scenarios.CreateDefaultFixture(graph, graph.Nodes[0].Id);
        guard.Armed = true;
        var result = (
            await f.Scenarios.RunAsync(graph, scenario, default)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var trace = await ReadTraceAsync(f, result.TraceId);
        var end = EndResult(trace, graph.Nodes[2].Id);
        end.Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        end.Event.TimeUtc.ShouldBe(scenario.ClockUtc);
        var send = EndResult(trace, graph.Nodes[3].Id);
        send.Event.TimeUtc.ShouldBe(scenario.ClockUtc.AddSeconds(1));
        end.Sequence.ShouldBeLessThan(send.Sequence);
        trace.Events[^1].Event.Kind.ShouldBe(AutomationTraceEventKind.Terminal);
        trace.Events[^1].Sequence.ShouldBeGreaterThan(send.Sequence);
        trace.Events[^1].Event.TimeUtc.ShouldBe(send.Event.TimeUtc);
        trace.Events.ShouldNotContain(item =>
            item.Event.Node != null
            && item.Event.Node.Id == graph.Nodes[2].Id
            && item.Event.Kind == AutomationTraceEventKind.Branch
        );
        scenario.Effects.ShouldNotContain(effect => effect.NodeId == graph.Nodes[2].Id);
        var invalid = (
            await f.Scenarios.RunAsync(
                graph,
                scenario with
                {
                    Effects =
                    [
                        .. scenario.Effects,
                        new(graph.Nodes[2].Id, AutomationScenarioEffectResult.Failed),
                    ],
                },
                default
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        invalid.Errors.ShouldContain(error => error.Code == "scenario-effect-invalid");
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        var raw = string.Join(
            "",
            await db.AutomationTraceEvents.Select(row => row.EventJson).ToArrayAsync()
        );
        raw.ShouldNotContain("sibling-private-value");
        guard.Writes.ShouldBe(0);
        guard.TraceWrites.ShouldBeGreaterThan(0);
        f.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task EndAutomation_SaveDuplicateAndActualTransferKeepExecutableV1Endpoint()
    {
        await using var f = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var end = Node("end-automation", "{}");
        var flow = (
            await f.Flows.SaveAsync(
                Draft(f.HostId, [source, end], [Edge(source, "flow", end)]),
                default
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var duplicate = (
            await f.Flows.DuplicateAsync(new(f.HostId), flow, default)
        ).ShouldBeOfType<AutomationFlowDuplicateOutcome.Duplicated>();
        var loaded = (
            await f.Flows.ListAsync(new(f.HostId), default)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        var copied = loaded.Flows.Single(value => value.Draft.Id == duplicate.FlowId).Draft;
        copied
            .Nodes.Single(node => node.Definition.TypeId == "end-automation")
            .Definition.SchemaVersion.ShouldBe(1);
        var document = (
            await new ConfigurationDocumentExporter(
                f.Database,
                new(),
                f.Catalog,
                f.Flows,
                f.Clock,
                f.Scenarios
            ).ExportAsync(
                f.HostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                    new(false, false, false)
                )
                {
                    AutomationFlowIds = new HashSet<Guid> { flow.Value },
                },
                default
            )
        )
            .ShouldBeOfType<ConfigurationExportOutcome.Success>()
            .Document;
        var selection = new ConfigurationImportSelection(
            f.HostId,
            [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags>()
        );
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var references = await ConfigurationImportReferencePlan.BuildAsync(
                db,
                f.HostId,
                document,
                selection,
                default
            );
            var host = await db.Hosts.SingleAsync(host => host.Id == f.HostId);
            var staged = await new AutomationConfigurationTransferAdapter(
                f.Flows,
                f.Catalog,
                f.Clock,
                f.Scenarios
            ).StageAsync(
                db,
                host,
                document.Sections.Automations!,
                selection.Sections[0],
                references,
                default
            );
            staged.Issues.ShouldBeEmpty();
            await transaction.CommitAsync();
        }
        var reloaded = (
            await f.Flows.ListAsync(new(f.HostId), default)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        var imported = reloaded.Flows.Single(value => value.Draft.Id == flow).Draft;
        var retained = imported.Nodes.Single(node => node.Definition.TypeId == "end-automation");
        var importedSource = imported.Nodes.Single(node =>
            node.Definition.TypeId == "custom-command"
        );
        retained.Definition.TypeId.ShouldBe("end-automation");
        retained.Definition.SchemaVersion.ShouldBe(1);
        retained.Definition.Configuration.ValueKind.ShouldBe(JsonValueKind.Object);
        retained.Definition.Configuration.EnumerateObject().ShouldBeEmpty();
        imported.Edges.ShouldBe([
            Edge(importedSource, "flow", retained) with
            {
                Id = imported.Edges.Single().Id,
            },
        ]);
        _ = (
            await f.Flows.SaveAsync(imported with { IsEnabled = true }, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatch = await f.NewRuntime()
            .DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            );
        dispatch.RunIds.Count().ShouldBe(1);
        (await f.NewRuntime().ResumeAsync(dispatch.RunIds.Single(), default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        f.Chat.Calls.ShouldBe(0);
    }

    [Test]
    [Arguments("flow")]
    [Arguments("data")]
    [Arguments("non-object")]
    [Arguments("extra-field")]
    public async Task EndAutomation_InvalidEditsCannotReplaceSavedGraphOrAdmitEffects(string edit)
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var end = Node("end-automation", "{}");
        var send = Node("send-chat", """{"message":"must not run"}""");
        var draft = Draft(f.HostId, [source, end], [Edge(source, "flow", end)]);
        var id = (await f.Flows.SaveAsync(draft, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var changed = edit switch
        {
            "flow" => draft with
            {
                Nodes = [source, end, send],
                Edges = [.. draft.Edges, Edge(end, "complete", send)],
            },
            "data" => draft with
            {
                Nodes = [source, end, send],
                Edges =
                [
                    .. draft.Edges,
                    Edge(end, "value", send, "message", AutomationEdgeKind.Data),
                ],
            },
            "non-object" => draft with
            {
                Nodes = [source, end with { Definition = Persisted("end-automation", "null") }],
            },
            "extra-field" => draft with
            {
                Nodes =
                [
                    source,
                    end with
                    {
                        Definition = Persisted("end-automation", """{"complete":true}"""),
                    },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };
        _ = (
            await f.Flows.SaveAsync(changed with { Id = id }, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        var loaded = (await f.Flows.ListAsync(new(f.HostId), default))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.ShouldHaveSingleItem()
            .Draft;
        loaded
            .Nodes.OrderBy(node => node.Id.Value)
            .Select(node =>
                (
                    node.Id,
                    node.Definition.TypeId,
                    node.Definition.SchemaVersion,
                    node.Definition.Configuration.GetRawText()
                )
            )
            .ShouldBe(
                draft
                    .Nodes.OrderBy(node => node.Id.Value)
                    .Select(node =>
                        (
                            node.Id,
                            node.Definition.TypeId,
                            node.Definition.SchemaVersion,
                            node.Definition.Configuration.GetRawText()
                        )
                    )
            );
        loaded.Edges.ShouldBe(draft.Edges);
        var run = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        (await f.NewRuntime().ResumeAsync(run, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        f.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task EndAutomation_ChildCannotReplaceReturnButParentEndsAfterRealInvocation()
    {
        await using var f = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var library = Subflows(f);
        var child = Subflow(f.HostId);
        var end = Node("end-automation", "{}");
        var invalid = child with
        {
            Graph = child.Graph with
            {
                Nodes = [.. child.Graph.Nodes, end],
                Edges = [Edge(child.Graph.Nodes[0], "complete", end)],
            },
        };
        (await library.PublishAsync(invalid, default))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Invalid>()
            .Errors.ShouldContain(error =>
                error.NodeId == end.Id && error.Code == "subflow-exit-unreachable"
            );
        var revision = await Publish(library, child);
        var parent = Caller(f.HostId, revision);
        parent = parent with
        {
            Nodes = [.. parent.Nodes, end],
            Edges = [.. parent.Edges, Edge(parent.Nodes[1], "complete", end)],
        };
        _ = (
            await f.Flows.SaveAsync(parent, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var run = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        var trace = await ReadTraceAsync(f, new(run.Value));
        var exit = trace.Events.Single(item =>
            item.Event.Kind == AutomationTraceEventKind.SubflowExit
        );
        exit.Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        EndResult(trace, end.Id).Sequence.ShouldBeGreaterThan(exit.Sequence);
        trace.Events[^1].Event.Kind.ShouldBe(AutomationTraceEventKind.Terminal);
        trace.Events[^1].Event.Outcome.ShouldBe(AutomationTraceOutcome.Succeeded);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        f.Chat.Calls.ShouldBe(0);
    }

    private static AutomationFlowDraft EndFanout(int hostId)
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""") with
        {
            Id = new(new Guid(1, 0, 0, new byte[8])),
        };
        var end = Node("end-automation", "{}") with { Id = new(new Guid(2, 0, 0, new byte[8])) };
        var send = Node("send-chat", """{"message":"sibling-private-value"}""");
        return Draft(
            hostId,
            [source, delay, end, send],
            [Edge(source, "flow", delay), Edge(source, "flow", end), Edge(delay, "complete", send)]
        );
    }

    private static AutomationTraceEntry EndResult(
        AutomationTraceSnapshot trace,
        AutomationNodeId id
    ) =>
        trace
            .Events.Where(item =>
                item.Event.Node != null
                && item.Event.Node.Id == id
                && item.Event.Kind == AutomationTraceEventKind.Result
            )
            .ShouldHaveSingleItem();
}
