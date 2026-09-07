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
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    public async Task SubflowDrain_ImmediateExitWaitsForSiblingOutcomeAcrossRestart(
        bool siblingFails,
        bool callerContinues,
        bool innerContinues
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync(chatAdmissions: [!siblingFails]);
        var draft = FanoutSubflow(fixture.HostId, innerContinues: innerContinues);
        var revision = await Publish(Subflows(fixture), draft);
        var caller = Caller(fixture.HostId, revision, DrainValues());
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = callerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var parent = Node("send-chat", """{"message":"parent-after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, parent],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", parent)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
            run.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
            var waiting = run.NodeRuns.Single(node => node.NodeId == invoke.Id.Value);
            waiting.Status.ShouldBe(AutomationNodeRunStatus.Waiting);
            waiting.CompletedAtUtc.ShouldBeNull();
            waiting.OutputJson.ShouldBeNull();
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(1);
        }
        fixture.Chat.Calls.ShouldBe(0);
        (await ReadTraceAsync(fixture, new(runId.Value))).Events.ShouldNotContain(item =>
            item.Event.Kind == AutomationTraceEventKind.SubflowExit
        );
        _ = await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var invocationSucceeded = !siblingFails || innerContinues;
        var continues = invocationSucceeded || callerContinues;
        var result = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);
        result.Status.ShouldBe(
            continues ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
        );
        fixture.Chat.Messages.ShouldBe(continues ? ["inner", "parent-after"] : ["inner"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
            var completed = run.NodeRuns.Single(node => node.NodeId == invoke.Id.Value);
            completed.Status.ShouldBe(
                invocationSucceeded ? AutomationNodeRunStatus.Succeeded
                : callerContinues ? AutomationNodeRunStatus.ContinuedAfterFailure
                : AutomationNodeRunStatus.Failed
            );
            if (invocationSucceeded)
            {
                var outputs = AutomationDataValueSerialization
                    .RestoreOutputs(completed.OutputJson!)
                    .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>()
                    .Outputs;
                outputs
                    .ShouldHaveSingleItem()
                    .Value.Value.ShouldBe(new AutomationValue.Text("declared-output"));
            }
            else
            {
                completed.OutputJson.ShouldBeNull();
            }
            run.NodeRuns.ShouldNotContain(node =>
                node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
                || node.Status == AutomationNodeRunStatus.Waiting
            );
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        }
        var trace = await ReadTraceAsync(fixture, new(runId.Value));
        var exit = trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldHaveSingleItem();
        exit.Event.Outcome.ShouldBe(
            invocationSucceeded ? AutomationTraceOutcome.Succeeded : AutomationTraceOutcome.Failed
        );
        var inner = trace.Events.Single(item =>
            item.Event.Kind == AutomationTraceEventKind.Result
            && item.Event.Node?.Id == draft.Graph.Nodes[3].Id
        );
        exit.Sequence.ShouldBeGreaterThan(inner.Sequence);
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.Terminal)
            .ShouldBe(1);
        var parentAttempts = trace
            .Events.Where(item =>
                item.Event.Kind == AutomationTraceEventKind.Attempt
                && item.Event.Node?.Id == parent.Id
            )
            .ToArray();
        parentAttempts.Length.ShouldBe(continues ? 1 : 0);
        if (continues)
        {
            parentAttempts[0].Sequence.ShouldBeGreaterThan(exit.Sequence);
        }
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            result.Status
        );
        fixture.Chat.Calls.ShouldBe(continues ? 2 : 1);
    }

    [Test]
    public async Task SubflowDrain_NestedEarlyExitsWaitForDeepSiblingThroughDueSchedulerRestart()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var library = Subflows(fixture);
        var leaf = await Publish(library, FanoutSubflow(fixture.HostId));
        var outer = Subflow(fixture.HostId);
        var entry = outer.Graph.Nodes[0] with { Id = new(new Guid(1, 0, 0, new byte[8])) };
        var exit = outer.Graph.Nodes[1] with { Id = new(new Guid(2, 0, 0, new byte[8])) };
        var child = Invoke(leaf, new(new Guid(3, 0, 0, new byte[8])), DrainValues());
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
        var caller = Caller(fixture.HostId, revision);
        var parent = Node("send-chat", """{"message":"parent-after"}""");
        caller = caller with
        {
            Nodes = [.. caller.Nodes, parent],
            Edges = [.. caller.Edges, Edge(caller.Nodes[1], "complete", parent)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var admitted = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var id = admitted.RunIds.ShouldHaveSingleItem();
        await fixture.NewRuntime().ResumeDueAsync(CancellationToken.None);
        fixture.Chat.Calls.ShouldBe(0);
        var waiting = await ReadTraceAsync(fixture, new(id.Value));
        waiting
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowEntry)
            .ShouldBe(2);
        waiting.Events.ShouldNotContain(item =>
            item.Event.Kind == AutomationTraceEventKind.SubflowExit
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.NewRuntime().ResumeDueAsync(CancellationToken.None);
        fixture.Chat.Messages.ShouldBe(["inner", "parent-after"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
            AutomationFlowRunStatus.Completed
        );
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        var trace = await ReadTraceAsync(fixture, new(id.Value));
        var exits = trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ToArray();
        exits.Length.ShouldBe(2);
        exits[0].Event.Invocation.SubflowId.ShouldBe(leaf.SubflowId.Value);
        exits[1].Event.Invocation.SubflowId.ShouldBe(revision.SubflowId.Value);
        trace
            .Events.Single(item =>
                item.Event.Kind == AutomationTraceEventKind.Attempt
                && item.Event.Node?.Id == parent.Id
            )
            .Sequence.ShouldBeGreaterThan(exits[1].Sequence);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SubflowDrain_ScenarioWaitsForSupportedSiblingEvaluationWithoutNestedFixtureOverrides(
        bool siblingFails,
        bool callerContinues
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var draft = FanoutSubflow(fixture.HostId, deterministicFailure: siblingFails);
        var revision = await Publish(Subflows(fixture), draft);
        var caller = Caller(fixture.HostId, revision, DrainValues());
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = callerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var parent = Node("send-chat", """{"message":"parent-after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, parent],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", parent)],
        };
        writes.Armed = true;
        var scenario = await fixture.Scenarios.RunAsync(
            caller,
            fixture.Scenarios.CreateDefaultFixture(caller, caller.Nodes[0].Id),
            CancellationToken.None
        );
        var traceId = scenario switch
        {
            AutomationScenarioRunOutcome.Completed completed => completed.TraceId,
            AutomationScenarioRunOutcome.Failed failed => failed.TraceId,
            _ => throw new InvalidOperationException("The valid scenario was not evaluated."),
        };
        if (siblingFails && !callerContinues)
        {
            _ = scenario.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
        }
        else
        {
            _ = scenario.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        }
        writes.Writes.ShouldBe(0);
        fixture.Chat.Calls.ShouldBe(0);
        var trace = await ReadTraceAsync(fixture, traceId);
        var exit = trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldHaveSingleItem();
        exit.Event.Outcome.ShouldBe(
            siblingFails ? AutomationTraceOutcome.Failed : AutomationTraceOutcome.Succeeded
        );
        exit.Sequence.ShouldBeGreaterThan(
            trace
                .Events.Single(item =>
                    item.Event.Kind == AutomationTraceEventKind.Result
                    && item.Event.Node?.Id == draft.Graph.Nodes[3].Id
                )
                .Sequence
        );
        var parentAttempts = trace
            .Events.Where(item =>
                item.Event.Kind == AutomationTraceEventKind.Attempt
                && item.Event.Node?.Id == parent.Id
            )
            .ToArray();
        parentAttempts.Length.ShouldBe(!siblingFails || callerContinues ? 1 : 0);
        if (parentAttempts.Length > 0)
        {
            parentAttempts[0].Sequence.ShouldBeGreaterThan(exit.Sequence);
        }
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.Terminal)
            .ShouldBe(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SubflowDrain_CancellationAfterLastSiblingCommitLeavesExitResumableByExistingScheduler(
        bool restart
    )
    {
        using var cancellation = new CancellationTokenSource();
        var cut = new DrainCommitCancellation(cancellation);
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [cut]);
        var revision = await Publish(Subflows(fixture), FanoutSubflow(fixture.HostId));
        var caller = Caller(fixture.HostId, revision, DrainValues());
        var parent = Node("send-chat", """{"message":"parent-after"}""");
        caller = caller with
        {
            Nodes = [.. caller.Nodes, parent],
            Edges = [.. caller.Edges, Edge(caller.Nodes[1], "complete", parent)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var admitted = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var id = admitted.RunIds.ShouldHaveSingleItem();
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        cut.Armed = true;
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Runtime.ResumeAsync(id, cancellation.Token)
        );
        cut.Armed.ShouldBeFalse();
        fixture.Chat.Messages.ShouldBe(["inner"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
            run.ExecutionLeaseId.ShouldBeNull();
            run.NodeRuns.ShouldNotContain(node =>
                node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
            );
            var boundary = run.NodeRuns.Single(node => node.NodeId == caller.Nodes[1].Id.Value);
            boundary.Status.ShouldBe(AutomationNodeRunStatus.Waiting);
            boundary.OutputJson.ShouldBeNull();
        }
        await (restart ? fixture.NewRuntime() : fixture.Runtime).ResumeDueAsync(
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBe(["inner", "parent-after"]);
        var trace = await ReadTraceAsync(fixture, new(id.Value));
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(1);
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.Terminal)
            .ShouldBe(1);
        await using var terminal = await fixture.Database.CreateDbContextAsync();
        (await terminal.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
            AutomationFlowRunStatus.Completed
        );
        (await terminal.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
    }

    private sealed class DrainCommitCancellation(CancellationTokenSource cancellation)
        : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            var nodes = eventData
                .Context!.ChangeTracker.Entries<AutomationNodeRun>()
                .Select(entry => entry.Entity)
                .ToArray();
            if (
                Armed
                && nodes.Any(node =>
                    node.Status == AutomationNodeRunStatus.Succeeded
                    && node.OutcomeCode == "action-succeeded"
                )
                && nodes.Any(node => node.Status == AutomationNodeRunStatus.Waiting)
                && nodes.All(node =>
                    node.Status
                        is not (AutomationNodeRunStatus.Pending or AutomationNodeRunStatus.Running)
                )
            )
            {
                Armed = false;
                cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SubflowDrain_FailedExitContinueCannotBypassCallerFailurePolicy(
        bool callerContinues,
        bool scenario
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var draft = FanoutSubflow(fixture.HostId);
        var entry = draft.Graph.Nodes[0];
        var exit = draft.Graph.Nodes[1] with
        {
            FailurePolicy = AutomationNodeFailurePolicy.Continue,
        };
        var random = Node("random-number", """{"minimum":7,"maximum":7}""");
        var transform = NumberTextTransform(
            "Invalid output precision",
            "format_number(number, int(number))"
        );
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [entry, exit, .. draft.Graph.Nodes.Skip(2), random, transform],
                Edges =
                [
                    .. draft.Graph.Edges.Where(edge => edge.Kind != AutomationEdgeKind.Data),
                    Edge(random, "number", transform, "number", AutomationEdgeKind.Data),
                    Edge(transform, "message", exit, "result", AutomationEdgeKind.Data),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), draft);
        var caller = Caller(fixture.HostId, revision, DrainValues());
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = callerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var parent = Node("send-chat", """{"message":"parent-after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, parent],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", parent)],
        };
        AutomationTraceId traceId;
        if (scenario)
        {
            var evaluated = await fixture.Scenarios.RunAsync(
                caller,
                fixture.Scenarios.CreateDefaultFixture(caller, caller.Nodes[0].Id),
                CancellationToken.None
            );
            traceId = callerContinues
                ? evaluated.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>().TraceId
                : evaluated.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>().TraceId;
            fixture.Chat.Calls.ShouldBe(0);
        }
        else
        {
            _ = (
                await fixture.Flows.SaveAsync(caller, CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            var admitted = await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            );
            var id = admitted.RunIds.ShouldHaveSingleItem();
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            (await fixture.Runtime.ResumeAsync(id, CancellationToken.None)).Status.ShouldBe(
                callerContinues ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
            );
            fixture.Chat.Messages.ShouldBe(callerContinues ? ["inner", "parent-after"] : ["inner"]);
            traceId = new(id.Value);
            await using var db = await fixture.Database.CreateDbContextAsync();
            (
                await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == invoke.Id.Value)
            ).OutputJson.ShouldBeNull();
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        }
        var trace = await ReadTraceAsync(fixture, traceId);
        var failedExit = trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldHaveSingleItem();
        failedExit.Event.Outcome.ShouldBe(AutomationTraceOutcome.Failed);
        trace
            .Events.Where(item =>
                item.Event.Kind == AutomationTraceEventKind.Result
                && item.Event.Node?.Id == invoke.Id
            )
            .ShouldHaveSingleItem()
            .Event.Outcome.ShouldBe(
                callerContinues
                    ? AutomationTraceOutcome.ContinuedAfterFailure
                    : AutomationTraceOutcome.Failed
            );
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.Terminal)
            .ShouldBe(1);
        var attempts = trace
            .Events.Where(item =>
                item.Event.Kind == AutomationTraceEventKind.Attempt
                && item.Event.Node?.Id == parent.Id
            )
            .ToArray();
        attempts.Length.ShouldBe(callerContinues ? 1 : 0);
        if (callerContinues)
        {
            attempts[0].Sequence.ShouldBeGreaterThan(failedExit.Sequence);
        }
    }

    private static ImmutableDictionary<AutomationPortId, AutomationValue> DrainValues() =>
        ImmutableDictionary<AutomationPortId, AutomationValue>.Empty.Add(
            new("result"),
            new AutomationValue.Text("declared-output")
        );

    private static AutomationSubflowDraft FanoutSubflow(
        int hostId,
        bool deterministicFailure = false,
        bool innerContinues = false
    )
    {
        var port = SubflowPort("result", AutomationPortValueType.Text);
        var draft = Subflow(hostId, new([port], [port]));
        var entry = draft.Graph.Nodes[0] with { Id = new(new Guid(1, 0, 0, new byte[8])) };
        var exit = draft.Graph.Nodes[1] with { Id = new(new Guid(2, 0, 0, new byte[8])) };
        var delay = Node("delay", """{"duration-milliseconds":1000}""") with
        {
            Id = new(new Guid(3, 0, 0, new byte[8])),
        };
        var action = Node(
            "send-chat",
            """{"message":"inner"}""",
            bindings: Bindings(
                "message",
                deterministicFailure
                    ? AutomationInputBindingMode.Connected
                    : AutomationInputBindingMode.Fixed
            )
        ) with
        {
            Id = new(new Guid(4, 0, 0, new byte[8])),
            FailurePolicy = innerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var random = Node("random-number", """{"minimum":7,"maximum":7}""");
        var transform = NumberTextTransform("Failure", "format_number(number, int(number))");
        return draft with
        {
            Graph = draft.Graph with
            {
                Nodes =
                [
                    entry,
                    exit,
                    delay,
                    action,
                    .. deterministicFailure ? new[] { random, transform } : [],
                ],
                Edges =
                [
                    Edge(entry, "complete", exit),
                    Edge(entry, "complete", delay),
                    Edge(delay, "complete", action),
                    Edge(action, "complete", exit),
                    Edge(entry, "result", exit, "result", AutomationEdgeKind.Data),
                    .. deterministicFailure
                        ? new[]
                        {
                            Edge(random, "number", transform, "number", AutomationEdgeKind.Data),
                            Edge(transform, "message", action, "message", AutomationEdgeKind.Data),
                        }
                        : [],
                ],
            },
        };
    }
}
