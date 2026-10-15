using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data.Common;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Scheduling_ReadyDescendantBypassesEarlierFutureContinuationWithoutEarlyOrRepeatedEffects()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        await f.Runtime.InitializeAsync(default);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":10000}""") with
        {
            Id = new(new Guid(1, 0, 0, new byte[8])),
        };
        var condition = Node("condition", """{"predicate":true}""") with
        {
            Id = new(new Guid(2, 0, 0, new byte[8])),
        };
        var delayed = Node("send-chat", """{"message":"long continuation"}""");
        var ready = Node("send-chat", """{"message":"ready descendant"}""");
        _ = await f.SaveAsync(
            [source, delay, condition, delayed, ready],
            [
                Edge(source, "flow", delay),
                Edge(source, "flow", condition),
                Edge(delay, "complete", delayed),
                Edge(condition, "yes", ready),
            ]
        );
        var now = f.Clock.GetUtcNow();
        var runId = await SchedulingDispatchAsync(f);
        f.Chat.Messages.ShouldBe(["ready descendant"]);
        var waiting = await SchedulingStoredRunAsync(f);
        waiting.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
        waiting.CompletedAtUtc.ShouldBeNull();
        waiting.ExecutionLeaseId.ShouldBeNull();
        var future = waiting.NodeRuns.Single(n => n.NodeId == delayed.Id.Value);
        var progressed = waiting.NodeRuns.Single(n => n.NodeId == ready.Id.Value);
        future.Sequence.ShouldBeLessThan(progressed.Sequence);
        future.Status.ShouldBe(AutomationNodeRunStatus.Pending);
        future.AvailableAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
        progressed.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
        progressed.CompletedAtUtc.ShouldBe(now.UtcDateTime);
        var earlyTrace = await ReadTraceAsync(f, new(runId.Value));
        earlyTrace
            .Events.Single(e => e.Event.Kind == AutomationTraceEventKind.Delay)
            .Sequence.ShouldBeLessThan(
                earlyTrace
                    .Events.Single(e =>
                        e.Event.Kind == AutomationTraceEventKind.Result
                        && e.Event.Node?.Id == ready.Id
                    )
                    .Sequence
            );
        earlyTrace.Events.ShouldNotContain(e => e.Event.Kind == AutomationTraceEventKind.Terminal);

        (await f.Runtime.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        f.Clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
        await f.Runtime.ResumeDueAsync(default);
        (await f.Runtime.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        f.Chat.Messages.ShouldBe(["ready descendant"]);
        (await SchedulingStoredRunAsync(f)).CompletedAtUtc.ShouldBeNull();
        f.Clock.Advance(TimeSpan.FromTicks(1));
        await f.Runtime.ResumeDueAsync(default);
        (await f.Runtime.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        await f.Runtime.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["ready descendant", "long continuation"]);
        var completed = await SchedulingStoredRunAsync(f);
        completed.ExecutionLeaseId.ShouldBeNull();
        completed.CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
        completed
            .NodeRuns.Single(n => n.NodeId == delayed.Id.Value)
            .AvailableAtUtc.ShouldBe(future.AvailableAtUtc);
        var trace = await ReadTraceAsync(f, new(runId.Value));
        trace
            .Events.Where(e =>
                e.Event.Kind == AutomationTraceEventKind.Result && e.Event.Node?.Id == delayed.Id
            )
            .ShouldHaveSingleItem()
            .Event.TimeUtc.ShouldBe(now.AddSeconds(10));
        trace.Events.Count(e => e.Event.Kind == AutomationTraceEventKind.Terminal).ShouldBe(1);
    }

    [Test]
    public async Task Scheduling_PersistedFutureWorkUsesFrozenGraphAndSequenceAmongReadyNotEarliestDeadline()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        await f.Runtime.InitializeAsync(default);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var longDelay = Node("delay", """{"duration-milliseconds":10000}""") with
        {
            Id = new(new Guid(1, 0, 0, new byte[8])),
        };
        var laterDue = Node("delay", """{"duration-milliseconds":2000}""") with
        {
            Id = new(new Guid(2, 0, 0, new byte[8])),
        };
        var earlierDue = Node("delay", """{"duration-milliseconds":1000}""") with
        {
            Id = new(new Guid(3, 0, 0, new byte[8])),
        };
        var longSend = Node("send-chat", """{"message":"long frozen"}""");
        var sequenceFirst = Node("send-chat", """{"message":"sequence first"}""");
        var deadlineFirst = Node("send-chat", """{"message":"earlier deadline"}""");
        var graph = Draft(
            f.HostId,
            [source, longDelay, laterDue, earlierDue, longSend, sequenceFirst, deadlineFirst],
            [
                Edge(source, "flow", longDelay),
                Edge(source, "flow", laterDue),
                Edge(source, "flow", earlierDue),
                Edge(longDelay, "complete", longSend),
                Edge(laterDue, "complete", sequenceFirst),
                Edge(earlierDue, "complete", deadlineFirst),
            ]
        );
        var flow = (await f.Flows.SaveAsync(graph, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var now = f.Clock.GetUtcNow();
        var runId = await SchedulingDispatchAsync(f);
        var before = await SchedulingStoredRunAsync(f);
        before.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
        before.ExecutionLeaseId.ShouldBeNull();
        before.CompletedAtUtc.ShouldBeNull();
        before.NodeRuns.Count(n => n.Status == AutomationNodeRunStatus.Pending).ShouldBe(3);
        before
            .NodeRuns.Where(n => n.Status == AutomationNodeRunStatus.Pending)
            .ShouldAllBe(n => n.AvailableAtUtc > now.UtcDateTime);
        var longPending = before.NodeRuns.Single(n => n.NodeId == longSend.Id.Value);
        var sequencePending = before.NodeRuns.Single(n => n.NodeId == sequenceFirst.Id.Value);
        var deadlinePending = before.NodeRuns.Single(n => n.NodeId == deadlineFirst.Id.Value);
        longPending.Sequence.ShouldBeLessThan(sequencePending.Sequence);
        sequencePending.Sequence.ShouldBeLessThan(deadlinePending.Sequence);
        sequencePending.AvailableAtUtc.ShouldBeGreaterThan(deadlinePending.AvailableAtUtc);
        f.Chat.Messages.ShouldBeEmpty();
        _ = (
            await f.Flows.SaveAsync(
                graph with
                {
                    Id = flow,
                    Nodes = graph
                        .Nodes.Select(n =>
                            n.Definition.TypeId == "send-chat"
                                ? Node("send-chat", """{"message":"edited live message"}""") with
                                {
                                    Id = n.Id,
                                }
                            : n.Definition.TypeId == "delay"
                                ? Node("delay", """{"duration-milliseconds":50000}""") with
                                {
                                    Id = n.Id,
                                }
                            : n
                        )
                        .ToImmutableArray(),
                },
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var resumed = f.NewRuntime();
        await resumed.InitializeAsync(default);
        f.Clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
        (await resumed.ResumeAsync(runId, default)).Status.ShouldBe(AutomationResumeStatus.Waiting);
        await resumed.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        await resumed.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["sequence first", "earlier deadline"]);
        var partial = await SchedulingStoredRunAsync(f);
        partial.DefinitionJson.ShouldBe(before.DefinitionJson);
        partial.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
        partial.CompletedAtUtc.ShouldBeNull();
        partial.ExecutionLeaseId.ShouldBeNull();
        partial
            .NodeRuns.Single(n => n.NodeId == longSend.Id.Value)
            .Status.ShouldBe(AutomationNodeRunStatus.Pending);
        partial
            .NodeRuns.Single(n => n.NodeId == longSend.Id.Value)
            .AvailableAtUtc.ShouldBe(longPending.AvailableAtUtc);
        partial
            .NodeRuns.Single(n => n.NodeId == sequenceFirst.Id.Value)
            .CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(2));
        partial
            .NodeRuns.Single(n => n.NodeId == deadlineFirst.Id.Value)
            .CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(2));
        f.Clock.Advance(TimeSpan.FromSeconds(8) - TimeSpan.FromTicks(1));
        (await resumed.ResumeAsync(runId, default)).Status.ShouldBe(AutomationResumeStatus.Waiting);
        f.Chat.Messages.Count.ShouldBe(2);
        f.Clock.Advance(TimeSpan.FromTicks(1));
        await resumed.ResumeDueAsync(default);
        (await resumed.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        await resumed.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["sequence first", "earlier deadline", "long frozen"]);
        var completed = await SchedulingStoredRunAsync(f);
        completed.DefinitionJson.ShouldBe(before.DefinitionJson);
        completed.CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
        completed.ExecutionLeaseId.ShouldBeNull();
    }

    [Test]
    public async Task Scheduling_ActualWorkerAdvancesDueSiblingAndRejectsConcurrentClaimsWithoutReplay()
    {
        var sender = new SchedulingHeldSender();
        var commits = new SchedulingCommitObserver();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [commits]
        );
        await f.Runtime.InitializeAsync(default);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var longDelay = Node("delay", """{"duration-milliseconds":10000}""") with
        {
            Id = new(new Guid(1, 0, 0, new byte[8])),
        };
        var shortDelay = Node("delay", """{"duration-milliseconds":1000}""") with
        {
            Id = new(new Guid(2, 0, 0, new byte[8])),
        };
        var longSend = Node("send-chat", """{"message":"long"}""");
        var shortSend = Node("send-chat", """{"message":"short"}""");
        _ = await f.SaveAsync(
            [source, longDelay, shortDelay, longSend, shortSend],
            [
                Edge(source, "flow", longDelay),
                Edge(source, "flow", shortDelay),
                Edge(longDelay, "complete", longSend),
                Edge(shortDelay, "complete", shortSend),
            ]
        );
        var runId = await SchedulingDispatchAsync(f);
        var now = f.Clock.GetUtcNow();
        var shortCheckpoint = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        commits.Observe = async () =>
        {
            var run = await SchedulingStoredRunAsync(f);
            if (
                run.NodeRuns.Any(n =>
                    n.NodeId == shortSend.Id.Value && n.Status == AutomationNodeRunStatus.Succeeded
                )
            )
            {
                _ = shortCheckpoint.TrySetResult();
            }
            if (run.Status == AutomationFlowRunStatus.Completed)
            {
                _ = completed.TrySetResult();
            }
        };
        var wake = new ManualTestTimeProvider(now);
        using var worker = new AutomationRuntimeWorker(f.Runtime, wake);
        await worker.StartAsync(default);
        try
        {
            _ = await wake.WaitForTimerRegistrationAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10));
            f.Clock.Advance(TimeSpan.FromSeconds(1));
            wake.Advance(TimeSpan.FromSeconds(1));
            await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var held = await SchedulingStoredRunAsync(f);
            var lease = held.ExecutionLeaseId.ShouldNotBeNull();
            held.NodeRuns.Single(n => n.NodeId == shortSend.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Running);
            held.NodeRuns.Single(n => n.NodeId == longSend.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Pending);
            sender.Accepted.ShouldBeEmpty();
            (await f.Runtime.ResumeAsync(runId, default)).Status.ShouldBe(
                AutomationResumeStatus.Waiting
            );
            await f.Runtime.ResumeDueAsync(default);
            (await SchedulingStoredRunAsync(f)).ExecutionLeaseId.ShouldBe(lease);
            sender.Entries.ShouldBe(["short"]);
            _ = sender.Release.TrySetResult();
            await shortCheckpoint.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var partial = await SchedulingStoredRunAsync(f);
            partial.CompletedAtUtc.ShouldBeNull();
            partial
                .NodeRuns.Single(n => n.NodeId == longSend.Id.Value)
                .AvailableAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
            partial
                .NodeRuns.Single(n => n.NodeId == shortSend.Id.Value)
                .CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(1));
            sender.Accepted.ShouldBe(["short"]);
            f.Clock.GetUtcNow().ShouldBe(wake.GetUtcNow());
            f.Clock.Advance(TimeSpan.FromSeconds(9));
            wake.Advance(TimeSpan.FromSeconds(9));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            _ = sender.Release.TrySetResult();
            await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        }
        var final = await SchedulingStoredRunAsync(f);
        final.Status.ShouldBe(AutomationFlowRunStatus.Completed);
        final.CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
        final.ExecutionLeaseId.ShouldBeNull();
        await f.Runtime.ResumeDueAsync(default);
        (await f.Runtime.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        sender.Entries.ShouldBe(["short", "long"]);
        sender.Accepted.ShouldBe(["short", "long"]);
        var trace = await ReadTraceAsync(f, new(runId.Value));
        trace.Events.Count(e => e.Event.Kind == AutomationTraceEventKind.Terminal).ShouldBe(1);
        trace.Events.ShouldNotContain(e => e.Event.Kind == AutomationTraceEventKind.Recovery);
    }

    [Test]
    public async Task Scheduling_NestedReturnsDrainOwnFutureDescendantWhileIndependentSiblingAdvances()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        await f.Runtime.InitializeAsync(default);
        var library = Subflows(f);
        var leaf = FanoutSubflow(f.HostId);
        var longDelay = leaf.Graph.Nodes[2];
        leaf = leaf with
        {
            Graph = leaf.Graph with
            {
                Nodes = leaf.Graph.Nodes.Replace(
                    longDelay,
                    Node("delay", """{"duration-milliseconds":10000}""") with
                    {
                        Id = longDelay.Id,
                    }
                ),
            },
        };
        var leafRevision = await Publish(library, leaf);
        var outer = Subflow(f.HostId);
        var entry = outer.Graph.Nodes[0] with { Id = new(new Guid(1, 0, 0, new byte[8])) };
        var exit = outer.Graph.Nodes[1] with { Id = new(new Guid(2, 0, 0, new byte[8])) };
        var child = Invoke(leafRevision, new(new Guid(3, 0, 0, new byte[8])), DrainValues());
        outer = outer with
        {
            Graph = outer.Graph with
            {
                Nodes = [entry, exit, child],
                Edges =
                [
                    Edge(entry, "complete", exit),
                    Edge(entry, "complete", child),
                    Edge(child, "complete", exit),
                ],
            },
        };
        var revision = await Publish(library, outer);
        var graph = Caller(f.HostId, revision);
        var invoke = graph.Nodes[1] with { Id = new(new Guid(1, 0, 0, new byte[8])) };
        var siblingDelay = Node("delay", """{"duration-milliseconds":1000}""") with
        {
            Id = new(new Guid(2, 0, 0, new byte[8])),
        };
        var siblingCondition = Node("condition", """{"predicate":true}""");
        var sibling = Node("send-chat", """{"message":"independent sibling"}""");
        var parentAfter = Node("send-chat", """{"message":"parent after return"}""");
        graph = graph with
        {
            Nodes = [graph.Nodes[0], invoke, siblingDelay, siblingCondition, sibling, parentAfter],
            Edges =
            [
                Edge(graph.Nodes[0], "flow", invoke),
                Edge(graph.Nodes[0], "flow", siblingDelay),
                Edge(siblingDelay, "complete", siblingCondition),
                Edge(siblingCondition, "yes", sibling),
                Edge(invoke, "complete", parentAfter),
            ],
        };
        _ = (
            await f.Flows.SaveAsync(graph, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var now = f.Clock.GetUtcNow();
        var runId = await SchedulingDispatchAsync(f);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Runtime.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["independent sibling"]);
        var waiting = await SchedulingStoredRunAsync(f);
        waiting.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
        waiting.CompletedAtUtc.ShouldBeNull();
        waiting.ExecutionLeaseId.ShouldBeNull();
        waiting
            .NodeRuns.Single(n => n.NodeId == invoke.Id.Value)
            .Status.ShouldBe(AutomationNodeRunStatus.Waiting);
        waiting.NodeRuns.ShouldNotContain(n => n.NodeId == parentAfter.Id.Value);
        var pending = waiting.NodeRuns.Single(n => n.Status == AutomationNodeRunStatus.Pending);
        pending.AvailableAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
        pending.Sequence.ShouldBeLessThan(
            waiting.NodeRuns.Single(n => n.NodeId == sibling.Id.Value).Sequence
        );
        var trace = await ReadTraceAsync(f, new(runId.Value));
        trace.Events.Count(e => e.Event.Kind == AutomationTraceEventKind.SubflowEntry).ShouldBe(2);
        trace.Events.ShouldNotContain(e =>
            e.Event.Kind == AutomationTraceEventKind.SubflowExit
            || e.Event.Kind == AutomationTraceEventKind.Terminal
        );
        f.Clock.Advance(TimeSpan.FromSeconds(9) - TimeSpan.FromTicks(1));
        var restarted = f.NewRuntime();
        await restarted.ResumeDueAsync(default);
        (await restarted.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        f.Chat.Messages.ShouldBe(["independent sibling"]);
        f.Clock.Advance(TimeSpan.FromTicks(1));
        await restarted.ResumeDueAsync(default);
        (await restarted.ResumeAsync(runId, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        await restarted.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["independent sibling", "inner", "parent after return"]);
        var final = await SchedulingStoredRunAsync(f);
        final.NodeRuns.ShouldAllBe(n => n.Status == AutomationNodeRunStatus.Succeeded);
        final.ExecutionLeaseId.ShouldBeNull();
        final.CompletedAtUtc.ShouldBe(now.UtcDateTime.AddSeconds(10));
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        var finalTrace = await ReadTraceAsync(f, new(runId.Value));
        var exits = finalTrace
            .Events.Where(e => e.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ToArray();
        exits.Length.ShouldBe(2);
        finalTrace
            .Events.Single(e =>
                e.Event.Kind == AutomationTraceEventKind.Result
                && e.Event.Node?.Id == parentAfter.Id
            )
            .Sequence.ShouldBeGreaterThan(exits[^1].Sequence);
    }

    private static async Task<AutomationRunId> SchedulingDispatchAsync(RuntimeFixture f) =>
        (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();

    private static async Task<AutomationFlowRun> SchedulingStoredRunAsync(RuntimeFixture f)
    {
        await using var db = await f.Database.CreateDbContextAsync();
        return await db.AutomationFlowRuns.AsNoTracking().Include(r => r.NodeRuns).SingleAsync();
    }

    private sealed class SchedulingHeldSender : IPublicChatMessageSender
    {
        internal ConcurrentQueue<string> Entries { get; } = [];
        internal ConcurrentQueue<string> Accepted { get; } = [];
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Entries.Enqueue(message);
            if (Entries.Count == 1)
            {
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            Accepted.Enqueue(message);
            return new PublicChatSendOutcome.Accepted();
        }
    }

    private sealed class SchedulingCommitObserver : DbTransactionInterceptor
    {
        internal Func<Task>? Observe { get; set; }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Observe is { } observe)
            {
                await observe();
            }
        }
    }
}
