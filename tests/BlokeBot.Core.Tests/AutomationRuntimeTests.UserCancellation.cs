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
    public async Task UserCancellation_EnteredEffectMayFinishButNeitherSiblingNorContinuationInvokes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingChatSender(
            null,
            async (_, _, token) =>
            {
                _ = entered.TrySetResult();
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
            ]
        );
        var dispatch = f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var id = new AutomationRunId(await SingleRunIdAsync(f));
            (await f.Runtime.CancelChannelRunsAsync(new(f.HostId), default)).ShouldBe(1);
            release.SetResult();
            _ = await dispatch;
            sender.Messages.ShouldBe(["entered"]);
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
