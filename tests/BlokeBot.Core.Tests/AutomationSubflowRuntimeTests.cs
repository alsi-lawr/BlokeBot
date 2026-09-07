using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task SubflowRuntime_RepeatedNestedCallsKeepOneRunAndCollisionFreeAuthoredAttribution()
    {
        await using var fixture = await RuntimeFixture.CreateAsync(migrateSchema: true);
        var library = Subflows(fixture);
        var leafDraft = Subflow(fixture.HostId);
        var action = Node("send-chat", """{"message":"inner"}""");
        leafDraft = leafDraft with
        {
            Graph = leafDraft.Graph with
            {
                Nodes = [leafDraft.Graph.Nodes[0], action, leafDraft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leafDraft.Graph.Nodes[0], "complete", action),
                    Edge(action, "complete", leafDraft.Graph.Nodes[1]),
                ],
            },
        };
        var leaf = await Publish(library, leafDraft);
        var outer = await Publish(library, Nesting(Subflow(fixture.HostId), leaf));
        var caller = Caller(fixture.HostId, outer);
        var first = caller.Nodes[1] with { Id = leaf.Graph.Nodes[0].Id };
        var second = Invoke(outer, new(new Guid(1, 0, 0, new byte[8])));
        caller = caller with
        {
            Nodes = [caller.Nodes[0], first, second],
            Edges = [Edge(caller.Nodes[0], "flow", first), Edge(first, "complete", second)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatch = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        dispatch.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        var runId = dispatch.RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBe(["inner", "inner"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = (
            await db.AutomationFlowRuns.Include(run => run.NodeRuns).ToArrayAsync()
        ).ShouldHaveSingleItem();
        run.Status.ShouldBe(AutomationFlowRunStatus.Completed);
        run.NodeRuns.Select(node => node.NodeId).Distinct().Count().ShouldBe(run.NodeRuns.Count);
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        var trace = await ReadTraceAsync(fixture, new(runId.Value));
        var entries = trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.SubflowEntry)
            .ToArray();
        entries.Length.ShouldBe(4);
        entries.Select(item => item.Event.Invocation.Id).Distinct().Count().ShouldBe(4);
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(4);
        var innerEvents = trace
            .Events.Where(item =>
                item.Event.Kind == AutomationTraceEventKind.Attempt
                && item.Event.Node?.Id == action.Id
            )
            .ToArray();
        innerEvents.Length.ShouldBe(2);
        innerEvents.Select(item => item.Event.Invocation.Id).Distinct().Count().ShouldBe(2);
        innerEvents.ShouldAllBe(item =>
            item.Event.Invocation.SubflowId == leaf.SubflowId.Value
            && item.Event.Invocation.RevisionId == leaf.Id.Value
        );
        var history = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        history
            .Runs.ShouldHaveSingleItem()
            .Nodes.Count(node => node.AuthorNodeId == action.Id)
            .ShouldBe(2);
    }

    [Test]
    public async Task SubflowRuntime_TypedBoundariesAndUnsavedScenarioPreserveValuesWithoutProductionEffects()
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var ports = ImmutableArray.Create(
            SubflowPort("text", AutomationPortValueType.Text),
            SubflowPort("map", AutomationPortValueType.Map),
            SubflowPort("array", AutomationPortValueType.Array),
            SubflowPort("nullable", AutomationPortValueType.Number) with
            {
                Nullability = AutomationPortNullability.Nullable,
            }
        );
        var revision = await Publish(Subflows(fixture), Subflow(fixture.HostId, new(ports, ports)));
        var values = new Dictionary<AutomationPortId, AutomationValue>
        {
            [new("text")] = new AutomationValue.Text("private-boundary-text"),
            [new("map")] = new AutomationValue.Map([new("nested", new AutomationValue.Number(12))]),
            [new("array")] = new AutomationValue.Array([new AutomationValue.Boolean(true)]),
            [new("nullable")] = new AutomationValue.Null(AutomationPortValueType.Number),
        }.ToImmutableDictionary();
        var caller = Caller(fixture.HostId, revision, values);
        var send = Node(
            "send-chat",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        caller = caller with
        {
            Nodes = [.. caller.Nodes, send],
            Edges =
            [
                .. caller.Edges,
                Edge(caller.Nodes[1], "complete", send),
                Edge(caller.Nodes[1], "text", send, "message", AutomationEdgeKind.Data),
            ],
        };
        writes.Armed = true;
        var scenario = (
            await fixture.Scenarios.RunAsync(
                caller,
                fixture.Scenarios.CreateDefaultFixture(caller, caller.Nodes[0].Id),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        writes.Writes.ShouldBe(0);
        fixture.Chat.Calls.ShouldBe(0);
        writes.Armed = false;
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatch = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        dispatch.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldBe(["private-boundary-text"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
            node.NodeId == caller.Nodes[1].Id.Value
        );
        var restored = AutomationDataValueSerialization
            .RestoreOutputs(checkpoint.OutputJson!)
            .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>();
        restored.Outputs.Keys.ShouldBe(values.Keys, ignoreOrder: true);
        foreach (var port in values.Keys)
        {
            if (values[port] is AutomationValue.Null expectedNull)
            {
                restored.Outputs[port].Value.ShouldBe(expectedNull);
            }
            else
            {
                AutomationStructuredValue
                    .Serialize(restored.Outputs[port].Value)
                    .ShouldBe(AutomationStructuredValue.Serialize(values[port]));
            }
        }
        var trace = await ReadTraceAsync(fixture, scenario.TraceId);
        JsonSerializer.Serialize(trace).ShouldNotContain("private-boundary-text");
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowEntry)
            .ShouldBe(1);
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SubflowRuntime_FailurePolicyUnwindsToCallerWithoutRetry(
        bool innerContinues,
        bool callerContinues
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync(chatAdmissions: [false]);
        var subflow = Subflow(fixture.HostId);
        var failure = Node("send-chat", """{"message":"inner"}""") with
        {
            FailurePolicy = innerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        subflow = subflow with
        {
            Graph = subflow.Graph with
            {
                Nodes = [subflow.Graph.Nodes[0], failure, subflow.Graph.Nodes[1]],
                Edges =
                [
                    Edge(subflow.Graph.Nodes[0], "complete", failure),
                    Edge(failure, "complete", subflow.Graph.Nodes[1]),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), subflow);
        var caller = Caller(fixture.HostId, revision);
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = callerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var after = Node("send-chat", """{"message":"after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, after],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", after)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatch = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var run = dispatch.RunIds.ShouldHaveSingleItem();
        var expected =
            innerContinues || callerContinues
                ? AutomationResumeStatus.Completed
                : AutomationResumeStatus.Failed;
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            expected
        );
        fixture.Chat.Messages.ShouldBe(
            innerContinues || callerContinues ? ["inner", "after"] : ["inner"]
        );
        var trace = await ReadTraceAsync(fixture, new(run.Value));
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(1);
        trace
            .Events.Where(item => item.Event.Outcome == AutomationTraceOutcome.Failed)
            .ShouldAllBe(item => item.Event.Retry == AutomationTraceRetry.NotRetried);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (
            await db.AutomationNodeRuns.AnyAsync(node =>
                node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
            )
        ).ShouldBeFalse();
    }

    [Test]
    public async Task SubflowRuntime_DelayFreezesRevisionsAndPureCheckpointsAcrossRebindDisableAndRestart()
    {
        var handler = TextValueHandler("test-counting-text-value", "frozen-value");
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [handler]);
        var draft = Subflow(fixture.HostId);
        var value = Node("test-counting-text-value", "{}");
        var delay = Node(
            "test-data-delay",
            """{"duration-milliseconds":1000}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], value, delay, action, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", delay),
                    Edge(delay, "complete", action),
                    Edge(action, "complete", draft.Graph.Nodes[1]),
                    Edge(value, "value", delay, "value", AutomationEdgeKind.Data),
                    Edge(value, "value", action, "message", AutomationEdgeKind.Data),
                ],
            },
        };
        var library = Subflows(fixture);
        var revision = await Publish(library, draft);
        var caller = Caller(fixture.HostId, revision);
        var flowId = (await fixture.Flows.SaveAsync(caller, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var dispatch = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatch.RunIds.ShouldHaveSingleItem();
        handler.Calls.ShouldBe(1);
        string definition;
        Guid[] executionIds;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
            run.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
            var invocation = run.NodeRuns.Single(node => node.NodeId == caller.Nodes[1].Id.Value);
            invocation.Status.ShouldBe(AutomationNodeRunStatus.Waiting);
            invocation.CompletedAtUtc.ShouldBeNull();
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(1);
            definition = run.DefinitionJson;
            executionIds = run.NodeRuns.Select(node => node.NodeId).ToArray();
        }
        var newer = await Publish(library, draft with { Description = "later revision" });
        _ = (
            await fixture.Flows.SaveAsync(
                caller with
                {
                    Id = flowId,
                    Nodes =
                    [
                        caller.Nodes[0],
                        caller.Nodes[1] with
                        {
                            Definition = AutomationSubflowDefinitions.Invocation(newer),
                        },
                    ],
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        (
            await library.RemoveRevisionAsync(
                new(fixture.HostId),
                revision.Id,
                CancellationToken.None
            )
        ).ShouldBe(AutomationSubflowRemovalOutcome.Referenced);
        _ = await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        handler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["frozen-value"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
            run.DefinitionJson.ShouldBe(definition);
            executionIds.ShouldAllBe(id => run.NodeRuns.Any(node => node.NodeId == id));
            run.NodeRuns.Single(node => node.NodeId == caller.Nodes[1].Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        }
        (
            await library.RemoveRevisionAsync(
                new(fixture.HostId),
                revision.Id,
                CancellationToken.None
            )
        ).ShouldBe(AutomationSubflowRemovalOutcome.Removed);
        _ = await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["frozen-value"]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubflowRuntime_CancelledInnerEffectUnwindsOnRestartWithoutReplayingCheckpointOrEffect(
        bool callerContinues
    )
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = InterruptFirstSend(entered);
        var handler = TextValueHandler("test-counting-text-value", "single-inner-attempt");
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat, handlers: [handler]);
        var draft = Subflow(fixture.HostId);
        var value = Node("test-counting-text-value", "{}");
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], value, action, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", action),
                    Edge(action, "complete", draft.Graph.Nodes[1]),
                    Edge(value, "value", action, "message", AutomationEdgeKind.Data),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), draft);
        var caller = Caller(fixture.HostId, revision);
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = callerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var after = Node("send-chat", """{"message":"after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, after],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", after)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        using var cancellation = new CancellationTokenSource();
        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            cancellation.Token
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var runId = await SingleRunIdAsync(fixture);
        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => dispatch);
        (
            await fixture.NewRuntime().ResumeAsync(new(runId), CancellationToken.None)
        ).Status.ShouldBe(
            callerContinues ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
        );
        handler.Calls.ShouldBe(1);
        chat.Messages.ShouldBe(
            callerContinues ? ["single-inner-attempt", "after"] : ["single-inner-attempt"]
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (
            await db.AutomationNodeRuns.AnyAsync(node =>
                node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
                || node.Status == AutomationNodeRunStatus.Waiting
            )
        ).ShouldBeFalse();
        var trace = await ReadTraceAsync(fixture, new(runId));
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubflowRuntime_AdmissionCancellationKeepsReferencesAtomicAndCommittedWorkResumable(
        bool afterCommit
    )
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new SubflowAdmissionCancellation(cancellation, afterCommit);
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [interceptor]
        );
        var revision = await Publish(Subflows(fixture), Subflow(fixture.HostId));
        var caller = Caller(fixture.HostId, revision);
        var send = Node("send-chat", """{"message":"once"}""");
        caller = caller with
        {
            Nodes = [.. caller.Nodes, send],
            Edges = [.. caller.Edges, Edge(caller.Nodes[1], "complete", send)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        await fixture.Runtime.InitializeAsync(CancellationToken.None);
        interceptor.Armed = true;
        var context = Context(fixture.HostId);
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                cancellation.Token
            )
        );
        interceptor.SawReferences.ShouldBeTrue();
        fixture.Chat.Calls.ShouldBe(0);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(afterCommit ? 1 : 0);
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(afterCommit ? 1 : 0);
            if (!afterCommit)
            {
                (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
                (await db.AutomationTraces.CountAsync()).ShouldBe(0);
            }
        }
        if (afterCommit)
        {
            (
                await fixture
                    .NewRuntime()
                    .ResumeAsync(new(await SingleRunIdAsync(fixture)), CancellationToken.None)
            ).Status.ShouldBe(AutomationResumeStatus.Completed);
        }
        else
        {
            (
                await fixture.Runtime.DispatchAsync(
                    new(context, new CustomCommandSourceConfiguration(new(7))),
                    CancellationToken.None
                )
            ).Status.ShouldBe(AutomationDispatchStatus.Accepted);
        }
        fixture.Chat.Messages.ShouldBe(["once"]);
        await using var terminal = await fixture.Database.CreateDbContextAsync();
        (await terminal.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task SubflowRuntime_ConcurrentRunsKeepIndependentInvocationCheckpointsAndOneTerminalEach()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var subflow = Subflow(fixture.HostId);
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var send = Node("send-chat", """{"message":"once-per-run"}""");
        subflow = subflow with
        {
            Graph = subflow.Graph with
            {
                Nodes = [subflow.Graph.Nodes[0], delay, send, subflow.Graph.Nodes[1]],
                Edges =
                [
                    Edge(subflow.Graph.Nodes[0], "complete", delay),
                    Edge(delay, "complete", send),
                    Edge(send, "complete", subflow.Graph.Nodes[1]),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), subflow);
        _ = (
            await fixture.Flows.SaveAsync(Caller(fixture.HostId, revision), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var first = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var second = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        first.RunIds.ShouldHaveSingleItem().ShouldNotBe(second.RunIds.ShouldHaveSingleItem());
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await Task.WhenAll(
            first
                .RunIds.Concat(second.RunIds)
                .Select(id => fixture.Runtime.ResumeAsync(id, CancellationToken.None))
        );
        resumed.ShouldAllBe(result => result.Status == AutomationResumeStatus.Completed);
        fixture.Chat.Messages.ShouldBe(["once-per-run", "once-per-run"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var runs = await db.AutomationFlowRuns.Include(run => run.NodeRuns).ToArrayAsync();
        runs.Length.ShouldBe(2);
        foreach (var run in runs)
        {
            run.NodeRuns.Select(node => node.NodeId)
                .Distinct()
                .Count()
                .ShouldBe(run.NodeRuns.Count);
            var trace = await ReadTraceAsync(fixture, new(run.Id));
            trace
                .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.Terminal)
                .ShouldBe(1);
            trace
                .Events.Single(item => item.Event.Kind == AutomationTraceEventKind.SubflowEntry)
                .Event.Invocation.ParentId.ShouldBe(run.Id);
        }
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubflowRuntime_TransitiveFeatureAndHostGenerationInvalidateWaitingBoundariesWithoutReplay(
        bool generationChange
    )
    {
        var overlays = new HostBoundOverlayCues();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var target = Guid.NewGuid();
        var cue = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, target, OverlayType.CuePlayer);
        overlays.AddCue(fixture.HostId, cue, OverlayCueQueuePolicy.Replace);
        var draft = Subflow(fixture.HostId);
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}"""
        );
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], delay, action, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", delay),
                    Edge(delay, "complete", action),
                    Edge(action, "complete", draft.Graph.Nodes[1]),
                ],
            },
        };
        var library = Subflows(fixture);
        var revision = await Publish(library, draft);
        var parent = await Publish(library, Nesting(Subflow(fixture.HostId), revision));
        _ = (
            await fixture.Flows.SaveAsync(Caller(fixture.HostId, parent), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var id = dispatched.RunIds.ShouldHaveSingleItem();
        if (generationChange)
        {
            await using var db = await fixture.Database.CreateDbContextAsync();
            _ = await db
                .Hosts.Where(host => host.Id == fixture.HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        host => host.AutomationGeneration,
                        host => host.AutomationGeneration + 1
                    )
                );
        }
        else
        {
            _ = await fixture.Features.DisableAsync(
                fixture.HostId,
                HostFeatureFlags.Overlays,
                CancellationToken.None
            );
        }
        (await fixture.Runtime.ResumeAsync(id, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Invalidated
        );
        overlays.Admissions.ShouldBeEmpty();
        _ = await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Overlays,
            CancellationToken.None
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await fixture.NewRuntime().ResumeAsync(id, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Invalidated
        );
        await using var terminal = await fixture.Database.CreateDbContextAsync();
        (await terminal.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (
            await terminal.AutomationNodeRuns.AnyAsync(node =>
                node.Status == AutomationNodeRunStatus.Waiting
                || node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
            )
        ).ShouldBeFalse();
        var trace = await ReadTraceAsync(fixture, new(id.Value));
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowEntry)
            .ShouldBe(2);
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(2);
        trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldAllBe(item => item.Event.Outcome == AutomationTraceOutcome.Invalidated);
    }

    [Test]
    public async Task SubflowRuntime_SensitiveBoundaryDataRemainsTypedAndAbsentFromScenarioDiagnosticsAndTraces()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var sensitive = SubflowPort("secret", AutomationPortValueType.Text) with
        {
            Sensitivity = AutomationDataSensitivity.Sensitive,
        };
        var revision = await Publish(
            Subflows(fixture),
            Subflow(fixture.HostId, new([sensitive], [sensitive]))
        );
        var values = ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
            sensitive.Id,
            new AutomationValue.Text("sensitive-boundary-value")
        );
        var caller = Caller(fixture.HostId, revision, values);
        var scenario = (
            await fixture.Scenarios.RunAsync(
                caller,
                fixture.Scenarios.CreateDefaultFixture(caller, caller.Nodes[0].Id),
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        JsonSerializer.Serialize(scenario).ShouldNotContain("sensitive-boundary-value");
        JsonSerializer
            .Serialize(await ReadTraceAsync(fixture, scenario.TraceId))
            .ShouldNotContain("sensitive-boundary-value");
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var id = dispatched.RunIds.ShouldHaveSingleItem();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
            node.NodeId == caller.Nodes[1].Id.Value
        );
        var outputs = AutomationDataValueSerialization
            .RestoreOutputs(checkpoint.OutputJson!)
            .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>()
            .Outputs;
        outputs[sensitive.Id].Value.ShouldBe(new AutomationValue.Text("sensitive-boundary-value"));
        outputs[sensitive.Id].ValueFreeDiagnostic.ShouldBeTrue();
        JsonSerializer
            .Serialize(await ReadTraceAsync(fixture, new(id.Value)))
            .ShouldNotContain("sensitive-boundary-value");
        var sink = Node(
            "send-chat",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        (
            await fixture.Flows.SaveAsync(
                caller with
                {
                    Nodes = [.. caller.Nodes, sink],
                    Edges =
                    [
                        .. caller.Edges,
                        Edge(caller.Nodes[1], "complete", sink),
                        Edge(caller.Nodes[1], "secret", sink, "message", AutomationEdgeKind.Data),
                    ],
                },
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "data-sensitivity-incompatible");
        fixture.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task SubflowRuntime_ExpansionWorkLimitRejectsBeforeAnyRootOrNestedEffectOrRunWrite()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var subflow = Subflow(fixture.HostId);
        var body = Enumerable
            .Range(0, 254)
            .Select(_ => Node("delay", """{"duration-milliseconds":1}"""))
            .ToArray();
        var nodes = new[] { subflow.Graph.Nodes[0] }
            .Concat(body)
            .Append(subflow.Graph.Nodes[1])
            .ToArray();
        subflow = subflow with
        {
            Graph = subflow.Graph with
            {
                Nodes = [.. nodes],
                Edges =
                [
                    .. nodes.Zip(
                        nodes.Skip(1),
                        (source, target) => Edge(source, "complete", target)
                    ),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), subflow);
        var caller = Caller(fixture.HostId, revision);
        var first = Node("send-chat", """{"message":"must-not-run"}""");
        var calls = Enumerable
            .Range(0, (AutomationFrozenSubflows.MaximumNodes / nodes.Length) + 1)
            .Select(_ => Invoke(revision))
            .ToArray();
        var execution = new[] { first }.Concat(calls).ToArray();
        caller = caller with
        {
            Nodes = [caller.Nodes[0], .. execution],
            Edges =
            [
                Edge(caller.Nodes[0], "flow", first),
                .. execution.Zip(
                    execution.Skip(1),
                    (source, target) => Edge(source, "complete", target)
                ),
            ],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        fixture.Chat.Calls.ShouldBe(0);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
    }

    private sealed class SubflowAdmissionCancellation(
        CancellationTokenSource cancellation,
        bool afterCommit
    ) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool SawReferences { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed)
            {
                SawReferences = eventData
                    .Context!.ChangeTracker.Entries<AutomationSubflowRunReference>()
                    .Any();
                if (!afterCommit)
                {
                    Armed = false;
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Armed && afterCommit)
            {
                Armed = false;
                cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
    }
}
