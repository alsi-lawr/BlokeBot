using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task UserCancellation_WaitingScopesRetainCommittedFactsAndTruncatedHistoryAcrossRestart()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var send = Node("send-chat", """{"message":"later"}""");
        var first = await f.SaveAsync(
            [source, delay, send],
            [Edge(source, "flow", delay), Edge(delay, "complete", send)]
        );
        var otherSource = Node("custom-command", """{"custom-command-id":7}""");
        var otherDelay = Node("delay", """{"duration-milliseconds":1000}""");
        var otherSend = Node("send-chat", """{"message":"unrelated"}""");
        var second = await f.SaveAsync(
            [otherSource, otherDelay, otherSend],
            [Edge(otherSource, "flow", otherDelay), Edge(otherDelay, "complete", otherSend)]
        );
        var dispatch = await f.Runtime.DispatchAsync(
            new(
                Context(f.HostId, sensitive: "cancel-private-sentinel"),
                new CustomCommandSourceConfiguration(new(7))
            ),
            default
        );
        dispatch.RunIds.Length.ShouldBe(2);
        Guid runId;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            runId = await db
                .AutomationFlowRuns.Where(run => run.FlowId == first.Value)
                .Select(run => run.Id)
                .SingleAsync();
            for (var index = 0; index < AutomationTraceStore.MaximumEvents; index++)
            {
                await AutomationTraceStore.AppendAsync(
                    db,
                    runId,
                    AutomationTraceRedaction.Event(
                        runId,
                        AutomationTraceEventKind.Resume,
                        f.Clock.GetUtcNow().UtcDateTime
                    ),
                    f.Clock.GetUtcNow().UtcDateTime,
                    default
                );
            }
        }
        (await f.Runtime.CancelRunAsync(new(f.HostId + 100), new(runId), default)).ShouldBe(0);
        (await f.Runtime.CancelFlowRunsAsync(new(f.HostId), first, default)).ShouldBe(1);
        (await f.Runtime.CancelRunAsync(new(f.HostId), new(runId), default)).ShouldBe(0);
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        await Task.WhenAll(f.Runtime.ResumeDueAsync(default), f.Runtime.ResumeDueAsync(default));
        var restarted = f.NewRuntime();
        await restarted.InitializeAsync(default);
        (await restarted.ResumeAsync(new(runId), default)).Status.ShouldBe(
            AutomationResumeStatus.Cancelled
        );
        f.Chat.Messages.ShouldBe(["unrelated"]);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var run = await db
                .AutomationFlowRuns.Include(run => run.NodeRuns)
                .SingleAsync(run => run.Id == runId);
            run.Status.ShouldBe(AutomationFlowRunStatus.Cancelled);
            run.ExecutionLeaseId.ShouldBeNull();
            run.NodeRuns.Single(node => node.NodeId == delay.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            run.NodeRuns.Single(node => node.NodeId == send.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Cancelled);
            (
                await db.AutomationFlowRuns.SingleAsync(run => run.FlowId == second.Value)
            ).Status.ShouldBe(AutomationFlowRunStatus.Completed);
            (
                await db.AutomationFlows.SingleAsync(flow => flow.Id == first.Value)
            ).IsEnabled.ShouldBeTrue();
            (
                await db.AutomationTraceEvents.Where(entry => entry.TraceId == runId).ToArrayAsync()
            ).ShouldAllBe(entry =>
                !entry.EventJson.Contains("cancel-private-sentinel", StringComparison.Ordinal)
            );
        }
        var trace = await ReadTraceAsync(f, new(runId));
        trace.Truncation.ShouldNotBe(AutomationTraceTruncation.None);
        trace.OutcomeSummary!.TerminalOutcome.ShouldBe(AutomationTraceOutcome.Cancelled);
        (await f.Runtime.CancelChannelRunsAsync(new(f.HostId), default)).ShouldBe(0);
    }

    [Test]
    public async Task UserCancellation_PostClaimInputsCannotEnterExecutorOrRestorePureCheckpoint()
    {
        var ready = new TaskCompletionSource<AutomationPureNodeResult.Succeeded>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var f = await RuntimeFixture.CreateAsync(
            transformDecorator: handler => new PausingPureHandler(handler, ready, release.Task)
        );
        var source = Node("test-number-source", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [],
                [
                    new(
                        "result",
                        "Result",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'prepared'"
                    ),
                ]
            )
        );
        var send = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await f.SaveAsync(
            [source, transform, send],
            [
                Edge(source, "flow", send),
                Edge(transform, "result", send, "message", AutomationEdgeKind.Data),
            ]
        );
        var dispatch = f.Runtime.DispatchAsync(
            new(
                Context(f.HostId, sourceDefinitionId: new("test-number-source")),
                new DataContractConfiguration()
            ),
            default
        );
        try
        {
            _ = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var id = new AutomationRunId(await SingleRunIdAsync(f));
            (await f.Runtime.CancelRunAsync(new(f.HostId), id, default)).ShouldBe(1);
            release.SetResult();
            _ = await dispatch;
            (await f.Runtime.ResumeAsync(id, default)).Status.ShouldBe(
                AutomationResumeStatus.Cancelled
            );
            f.Chat.Calls.ShouldBe(0);
            await using var db = await f.Database.CreateDbContextAsync();
            var nodes = await db
                .AutomationNodeRuns.Where(node => node.RunId == id.Value)
                .ToArrayAsync();
            nodes
                .Where(node => node.NodeId != source.Id.Value)
                .ShouldAllBe(node =>
                    node.Status == AutomationNodeRunStatus.Cancelled && node.OutputJson == null
                );
        }
        finally
        {
            _ = release.TrySetResult();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UserCancellation_RevokedLeaseRejectsLatePureBeginAndInputEvents(
        bool afterInnerCheckpoint
    )
    {
        var barrier = new RecoveryCommitBarrier();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        var source = Node("test-number-source", "{}");
        var inner = Node(
            "test-cel-transform",
            TransformJson(
                [],
                [
                    new(
                        "result",
                        "Result",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'prepared'"
                    ),
                ]
            )
        );
        var outer = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "input",
                        "value",
                        "Value",
                        "input",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        ""
                    ),
                ],
                [
                    new(
                        "result",
                        "Result",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "value"
                    ),
                ]
            ),
            bindings: Bindings("input", AutomationInputBindingMode.Connected)
        );
        var send = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await f.SaveAsync(
            [source, inner, outer, send],
            [
                Edge(source, "flow", send),
                Edge(inner, "result", outer, "input", AutomationEdgeKind.Data),
                Edge(outer, "result", send, "message", AutomationEdgeKind.Data),
            ]
        );
        barrier.Arm(async () =>
        {
            await using var db = await f.Database.CreateDbContextAsync();
            return afterInnerCheckpoint
                ? await db.AutomationNodeRuns.AnyAsync(node =>
                    node.NodeId == inner.Id.Value
                    && node.Status == AutomationNodeRunStatus.Succeeded
                )
                : await db.AutomationNodeRuns.AnyAsync(node =>
                    node.NodeId == send.Id.Value && node.Status == AutomationNodeRunStatus.Running
                );
        });
        var dispatch = f.Runtime.DispatchAsync(
            new(
                Context(f.HostId, sourceDefinitionId: new("test-number-source")),
                new DataContractConfiguration()
            ),
            default
        );
        try
        {
            await barrier.Committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var id = new AutomationRunId(await SingleRunIdAsync(f));
            (await f.Runtime.CancelRunAsync(new(f.HostId), id, default)).ShouldBe(1);
            await using var cancelled = await f.Database.CreateDbContextAsync();
            var nodes = await cancelled
                .AutomationNodeRuns.Where(node => node.RunId == id.Value)
                .OrderBy(node => node.NodeId)
                .Select(node => new
                {
                    node.NodeId,
                    node.Status,
                    node.OutcomeCode,
                    node.OutputJson,
                })
                .ToArrayAsync();
            var events = await cancelled
                .AutomationTraceEvents.Where(entry => entry.TraceId == id.Value)
                .OrderBy(entry => entry.Sequence)
                .Select(entry => entry.EventJson)
                .ToArrayAsync();
            nodes.ShouldAllBe(node =>
                node.Status == AutomationNodeRunStatus.Succeeded
                || node.Status == AutomationNodeRunStatus.Cancelled
            );
            if (afterInnerCheckpoint)
            {
                _ = nodes
                    .Single(node => node.NodeId == inner.Id.Value)
                    .OutputJson.ShouldNotBeNull();
                nodes
                    .Single(node => node.NodeId == outer.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Cancelled);
            }
            else
            {
                nodes.ShouldNotContain(node =>
                    node.NodeId == inner.Id.Value || node.NodeId == outer.Id.Value
                );
            }
            _ = barrier.Release.TrySetResult();
            _ = await dispatch;
            await f.NewRuntime().InitializeAsync(default);
            (await f.Runtime.ResumeAsync(id, default)).Status.ShouldBe(
                AutomationResumeStatus.Cancelled
            );
            f.Chat.Calls.ShouldBe(0);
            await using var verify = await f.Database.CreateDbContextAsync();
            (await verify.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                AutomationFlowRunStatus.Cancelled
            );
            (
                await verify
                    .AutomationNodeRuns.Where(node => node.RunId == id.Value)
                    .OrderBy(node => node.NodeId)
                    .Select(node => new
                    {
                        node.NodeId,
                        node.Status,
                        node.OutcomeCode,
                        node.OutputJson,
                    })
                    .ToArrayAsync()
            ).ShouldBe(nodes);
            (
                await verify
                    .AutomationTraceEvents.Where(entry => entry.TraceId == id.Value)
                    .OrderBy(entry => entry.Sequence)
                    .Select(entry => entry.EventJson)
                    .ToArrayAsync()
            ).ShouldBe(events);
        }
        finally
        {
            _ = barrier.Release.TrySetResult();
            _ = await dispatch;
        }
    }

    [Test]
    public async Task UserCancellation_RevokedLeasePreservesInvalidCachedOutputAfterLaterClaim()
    {
        var barrier = new RecoveryCommitBarrier();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        var source = Node("test-number-source", "{}");
        var producer = Node(
            "test-cel-transform",
            TransformJson(
                [],
                [
                    new(
                        "result",
                        "Result",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'committed'"
                    ),
                ]
            )
        );
        var first = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var later = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await f.SaveAsync(
            [source, producer, first, delay, later],
            [
                Edge(source, "flow", first),
                Edge(first, "complete", delay),
                Edge(delay, "complete", later),
                Edge(producer, "result", first, "message", AutomationEdgeKind.Data),
                Edge(producer, "result", later, "message", AutomationEdgeKind.Data),
            ]
        );
        var id = (
            await f.Runtime.DispatchAsync(
                new(
                    Context(f.HostId, sourceDefinitionId: new("test-number-source")),
                    new DataContractConfiguration()
                ),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.RunId == id.Value && node.NodeId == producer.Id.Value
            );
            checkpoint.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            checkpoint.OutputJson = "{}";
            _ = await db.SaveChangesAsync();
        }
        barrier.Arm(async () =>
        {
            await using var db = await f.Database.CreateDbContextAsync();
            return await db.AutomationNodeRuns.AnyAsync(node =>
                node.NodeId == later.Id.Value && node.Status == AutomationNodeRunStatus.Running
            );
        });
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        var resume = f.Runtime.ResumeAsync(id, default);
        try
        {
            await barrier.Committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await f.Runtime.CancelRunAsync(new(f.HostId), id, default)).ShouldBe(1);
            var trace = await ReadTraceAsync(f, new(id.Value));
            _ = barrier.Release.TrySetResult();
            (await resume).Status.ShouldBe(AutomationResumeStatus.Cancelled);
            await f.NewRuntime().InitializeAsync(default);
            await using var verify = await f.Database.CreateDbContextAsync();
            var producerCheckpoint = await verify.AutomationNodeRuns.SingleAsync(node =>
                node.RunId == id.Value && node.NodeId == producer.Id.Value
            );
            producerCheckpoint.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            producerCheckpoint.OutputJson.ShouldBe("{}");
            producerCheckpoint.OutcomeCode.ShouldBe("output-checkpointed");
            (await verify.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                AutomationFlowRunStatus.Cancelled
            );
            (
                await verify.AutomationNodeRuns.Where(node => node.RunId == id.Value).ToArrayAsync()
            ).ShouldAllBe(node =>
                node.Status == AutomationNodeRunStatus.Succeeded
                || node.Status == AutomationNodeRunStatus.Cancelled
            );
            (await ReadTraceAsync(f, new(id.Value))).Events.Length.ShouldBe(trace.Events.Length);
            f.Chat.Messages.ShouldBe(["committed"]);
        }
        finally
        {
            _ = barrier.Release.TrySetResult();
            _ = await resume;
        }
    }

    [Test]
    public async Task UserCancellation_EnteredEffectMayFinishButNeitherSiblingNorContinuationInvokes()
    {
        var entered = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingChatSender(
            null,
            async (call, message, token) =>
            {
                _ = entered.TrySetResult(message);
                await release.Task.WaitAsync(token);
            }
        );
        await using var f = await RuntimeFixture.CreateAsync(chat: sender);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var first = Node("send-chat", """{"message":"entered"}""");
        var later = Node("send-chat", """{"message":"later"}""");
        var sibling = Node("send-chat", """{"message":"sibling"}""");
        _ = await f.SaveAsync(
            [source, first, later, sibling],
            [
                Edge(source, "flow", first),
                Edge(source, "flow", sibling),
                Edge(first, "complete", later),
                Edge(sibling, "complete", later),
            ]
        );
        var dispatch = f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        try
        {
            var firstMessage = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var id = new AutomationRunId(await SingleRunIdAsync(f));
            (await f.Runtime.CancelChannelRunsAsync(new(f.HostId), default)).ShouldBe(1);
            release.SetResult();
            _ = await dispatch;
            sender.Messages.ShouldBe([firstMessage]);
            (await f.Runtime.ResumeAsync(id, default)).Status.ShouldBe(
                AutomationResumeStatus.Cancelled
            );
            await f.NewRuntime().ResumeDueAsync(default);
            sender.Calls.ShouldBe(1);
            var trace = await ReadTraceAsync(f, new(id.Value));
            trace.Events.ShouldContain(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Cancellation
                && entry.Event.Outcome == AutomationTraceOutcome.Cancelled
            );
            trace.OutcomeSummary!.TerminalOutcome.ShouldBe(AutomationTraceOutcome.Cancelled);
        }
        finally
        {
            _ = release.TrySetResult();
        }
    }

    [Test]
    public async Task UserCancellation_NestedWaitsCloseEveryInvocationAndRetireReferencesWithoutLaterEffects()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var leaf = Subflow(f.HostId);
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var send = Node("send-chat", """{"message":"nested later"}""");
        leaf = leaf with
        {
            Graph = leaf.Graph with
            {
                Nodes = [leaf.Graph.Nodes[0], delay, send, leaf.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leaf.Graph.Nodes[0], "complete", delay),
                    Edge(delay, "complete", send),
                    Edge(send, "complete", leaf.Graph.Nodes[1]),
                ],
            },
        };
        var library = Subflows(f);
        var published = await Publish(library, leaf);
        var outer = await Publish(library, Nesting(Subflow(f.HostId), published));
        var caller = Caller(f.HostId, outer);
        _ = (
            await f.Flows.SaveAsync(caller, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var id = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        (await f.Runtime.CancelRunAsync(new(f.HostId), id, default)).ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        await f.NewRuntime().ResumeDueAsync(default);
        f.Chat.Calls.ShouldBe(0);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.ToArrayAsync()).ShouldNotContain(node =>
            node.Status == AutomationNodeRunStatus.Pending
            || node.Status == AutomationNodeRunStatus.Running
            || node.Status == AutomationNodeRunStatus.Waiting
        );
        var trace = await ReadTraceAsync(f, new(id.Value));
        var exits = trace
            .Events.Where(entry => entry.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ToArray();
        exits.Length.ShouldBe(2);
        exits.ShouldAllBe(entry => entry.Event.Outcome == AutomationTraceOutcome.Cancelled);
    }
}
