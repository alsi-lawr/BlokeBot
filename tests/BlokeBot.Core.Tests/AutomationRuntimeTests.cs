using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationRuntimeTests
{
    [Test]
    public async Task FlowValidation_RejectsJoinsCyclesDisconnectedAndIncompatibleEdges()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var first = Node("send-chat", """{"message":"first"}""");
        var second = Node("send-chat", """{"message":"second"}""");
        var joined = Node("send-chat", """{"message":"joined"}""");
        var disconnected = Node("delay", """{"duration-milliseconds":1000}""");
        var edges = ImmutableArray.Create(
            Edge(source, "actor", first),
            Edge(source, "flow", second),
            Edge(first, "complete", joined),
            Edge(second, "complete", joined),
            Edge(joined, "complete", first)
        );

        var outcome = await fixture.Flows.SaveAsync(
            Draft(fixture.HostId, [source, first, second, joined, disconnected], edges),
            CancellationToken.None
        );

        var invalid = outcome.ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        invalid.Errors.Select(static error => error.Code).ShouldContain("join-not-supported");
        invalid.Errors.Select(static error => error.Code).ShouldContain("cycle");
        invalid.Errors.Select(static error => error.Code).ShouldContain("node-disconnected");
        invalid.Errors.Select(static error => error.Code).ShouldContain("port-incompatible");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Dispatch_ConditionsRouteDeterministicallyAndExternalIdentityDeduplicates()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var condition = Node("condition", """{"expression":"arguments[0] == 'yes'"}""");
        var matched = Node("send-chat", """{"message":"Hello ${actor.display_name}"}""");
        var unmatched = Node("send-chat", """{"message":"No match"}""");
        var flowId = await fixture.SaveAsync(
            [source, condition, matched, unmatched],
            [
                Edge(source, "flow", condition),
                Edge(condition, "true", matched),
                Edge(condition, "false", unmatched),
            ]
        );
        var context = Context(fixture.HostId, "yes", "private-argument");

        var first = await fixture.Runtime.DispatchAsync(
            new(context, new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var duplicate = await fixture.Runtime.DispatchAsync(
            new(context, new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        first.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        first.RunIds.Length.ShouldBe(1);
        duplicate.Status.ShouldBe(AutomationDispatchStatus.Duplicate);
        fixture.Chat.Messages.ShouldBe(["Hello Viewer"]);
        var query = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        var summary = query.Runs.ShouldHaveSingleItem();
        summary.FlowId.ShouldBe(flowId);
        summary.State.ShouldBe(AutomationFlowRunState.Completed);
        JsonSerializer.Serialize(summary).ShouldNotContain("private-argument");
        summary.Nodes.ShouldAllBe(static node =>
            node.OutcomeCode == "source-received"
            || node.OutcomeCode == "condition-true"
            || node.OutcomeCode == "action-succeeded"
        );
    }

    [Test]
    public async Task ConcurrentDispatch_SameOccurrenceCreatesOneRunAndExecutesOneAction()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"once"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var trigger = new AutomationTrigger(
            Context(fixture.HostId),
            new CustomCommandSourceConfiguration(new(7))
        );
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<AutomationDispatchOutcome> DispatchAsync()
        {
            await start.Task;
            return await fixture.Runtime.DispatchAsync(trigger, CancellationToken.None);
        }

        var first = DispatchAsync();
        var second = DispatchAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        outcomes
            .Select(static outcome => outcome.Status)
            .Order()
            .ShouldBe([AutomationDispatchStatus.Accepted, AutomationDispatchStatus.Duplicate]);
        fixture.Chat.Messages.ShouldBe(["once"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
        (
            await db.AutomationNodeRuns.CountAsync(static node =>
                node.OutcomeCode == "action-succeeded"
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentResumeAndWorker_MultipleBranchesExecuteSeriallyAndOnlyOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(
            null,
            async (call, _, cancellationToken) =>
            {
                if (call != 1)
                {
                    return;
                }

                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        );
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var first = Node("send-chat", """{"message":"first"}""");
        var second = Node("send-chat", """{"message":"second"}""");
        _ = await fixture.SaveAsync(
            [source, first, second],
            [Edge(source, "flow", first), Edge(source, "flow", second)]
        );

        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Guid runId;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            runId = await db.AutomationFlowRuns.Select(static run => run.Id).SingleAsync();
        }

        var concurrentResume = fixture.Runtime.ResumeAsync(new(runId), CancellationToken.None);
        var worker = fixture.Runtime.ResumeDueAsync(CancellationToken.None);
        (await concurrentResume.WaitAsync(TimeSpan.FromSeconds(5))).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        chat.Messages.Count.ShouldBe(1);

        release.SetResult();
        _ = await dispatch.WaitAsync(TimeSpan.FromSeconds(5));

        chat.Messages.Order().ShouldBe(["first", "second"]);
        await using var verified = await fixture.Database.CreateDbContextAsync();
        var actionRuns = await verified
            .AutomationNodeRuns.Where(node =>
                node.NodeId == first.Id.Value || node.NodeId == second.Id.Value
            )
            .ToArrayAsync();
        actionRuns.Length.ShouldBe(2);
        actionRuns.ShouldAllBe(static node =>
            node.Status == AutomationNodeRunStatus.Succeeded
            && node.OutcomeCode == "action-succeeded"
        );
    }

    [Test]
    public async Task DisableDuringAction_InvalidationRemainsTerminalAndEnqueuesNoContinuation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(
            null,
            async (call, _, cancellationToken) =>
            {
                if (call != 1)
                {
                    return;
                }

                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        );
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"in flight"}""");
        var never = Node("send-chat", """{"message":"must not replay"}""");
        _ = await fixture.SaveAsync(
            [source, action, never],
            [Edge(source, "flow", action), Edge(action, "complete", never)]
        );
        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runId = await SingleRunIdAsync(fixture);

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        release.SetResult();
        _ = await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );

        (
            await fixture.NewRuntime().ResumeAsync(new(runId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Invalidated);
        chat.Messages.ShouldBe(["in flight"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.Include(static value => value.NodeRuns).SingleAsync();
        run.Status.ShouldBe(AutomationFlowRunStatus.Invalidated);
        run.NodeRuns.ShouldNotContain(static node =>
            node.Status == AutomationNodeRunStatus.Pending
            || node.Status == AutomationNodeRunStatus.Running
        );
        run.NodeRuns.ShouldNotContain(node => node.NodeId == never.Id.Value);
    }

    [Test]
    public async Task DurableDelay_NewRuntimeResumesPersistedContinuationAfterRestart()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"After restart"}""");
        var flowId = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        var changedAction = action with
        {
            Definition = Persisted("send-chat", """{"message":"Changed after the run started"}"""),
        };
        _ = (
            await fixture.Flows.SaveAsync(
                Draft(
                    fixture.HostId,
                    [source, delay, changedAction],
                    [Edge(source, "flow", delay), Edge(delay, "complete", changedAction)]
                ) with
                {
                    Id = flowId,
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var restarted = fixture.NewRuntime();

        var resumed = await restarted.ResumeAsync(
            dispatched.RunIds.ShouldHaveSingleItem(),
            CancellationToken.None
        );

        resumed.Status.ShouldBe(AutomationResumeStatus.Completed);
        fixture.Chat.Messages.ShouldBe(["After restart"]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task DurableContext_UnsupportedSchemaTerminatesGenericallyWithoutExecution(
        int unsupportedVersion
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not execute"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            _ = await db
                .AutomationFlowRuns.Where(run => run.Id == runId.Value)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(static run => run.ContextSchemaVersion, unsupportedVersion)
                );
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        fixture.Chat.Messages.ShouldBeEmpty();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.Nodes.ShouldContain(static node =>
            node.OutcomeCode == "context-version-unsupported"
        );
        JsonSerializer.Serialize(summary).ShouldNotContain("must not execute");
    }

    [Test]
    public async Task FailurePolicies_StopOrContinueWithoutAutomaticRetries()
    {
        await using var stop = await RuntimeFixture.CreateAsync([false, true]);
        var stopSource = Node("custom-command", """{"custom-command-id":7}""");
        var stopFailure = Node("send-chat", """{"message":"reject"}""");
        var never = Node("send-chat", """{"message":"never"}""");
        _ = await stop.SaveAsync(
            [stopSource, stopFailure, never],
            [Edge(stopSource, "flow", stopFailure), Edge(stopFailure, "complete", never)]
        );

        var stopped = await stop.Runtime.DispatchAsync(
            new(Context(stop.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        stop.Chat.Messages.ShouldBe(["reject"]);
        (
            await stop.Runtime.ResumeAsync(
                stopped.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Failed);

        await using var continued = await RuntimeFixture.CreateAsync([false, true]);
        var continueSource = Node("custom-command", """{"custom-command-id":7}""");
        var continueFailure = Node(
            "send-chat",
            """{"message":"reject"}""",
            AutomationNodeFailurePolicy.Continue
        );
        var after = Node("send-chat", """{"message":"after"}""");
        _ = await continued.SaveAsync(
            [continueSource, continueFailure, after],
            [
                Edge(continueSource, "flow", continueFailure),
                Edge(continueFailure, "complete", after),
            ]
        );

        var completed = await continued.Runtime.DispatchAsync(
            new(Context(continued.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        var continuedSummary = (
            await continued.Queries.ListAsync(new(continued.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        continuedSummary
            .Nodes.Select(static node => (node.State, node.OutcomeCode))
            .ShouldBe([
                (AutomationNodeRunState.Succeeded, "source-received"),
                (AutomationNodeRunState.ContinuedAfterFailure, "chat-rejected"),
                (AutomationNodeRunState.Succeeded, "action-succeeded"),
            ]);
        continued.Chat.Messages.ShouldBe(["reject", "after"]);
        (
            await continued.Runtime.ResumeAsync(
                completed.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Completed);
    }

    [Test]
    public async Task InterruptedActions_RestartAppliesStopOrContinueWithoutRetryingTheAction()
    {
        var stopEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var stopChat = InterruptFirstSend(stopEntered);
        await using var stop = await RuntimeFixture.CreateAsync(chat: stopChat);
        var stopSource = Node("custom-command", """{"custom-command-id":7}""");
        var stopAction = Node("send-chat", """{"message":"stop interrupted"}""");
        _ = await stop.SaveAsync([stopSource, stopAction], [Edge(stopSource, "flow", stopAction)]);
        using var stopCancellation = new CancellationTokenSource();
        var stopDispatch = stop.Runtime.DispatchAsync(
            new(Context(stop.HostId), new CustomCommandSourceConfiguration(new(7))),
            stopCancellation.Token
        );
        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopRunId = await SingleRunIdAsync(stop);
        stopCancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => stopDispatch);

        (
            await stop.NewRuntime().ResumeAsync(new(stopRunId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Failed);
        stopChat.Messages.ShouldBe(["stop interrupted"]);

        var continueEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var continueChat = InterruptFirstSend(continueEntered);
        await using var continued = await RuntimeFixture.CreateAsync(chat: continueChat);
        var continueSource = Node("custom-command", """{"custom-command-id":7}""");
        var continueAction = Node(
            "send-chat",
            """{"message":"continue interrupted"}""",
            AutomationNodeFailurePolicy.Continue
        );
        var after = Node("send-chat", """{"message":"after restart"}""");
        _ = await continued.SaveAsync(
            [continueSource, continueAction, after],
            [Edge(continueSource, "flow", continueAction), Edge(continueAction, "complete", after)]
        );
        using var continueCancellation = new CancellationTokenSource();
        var continueDispatch = continued.Runtime.DispatchAsync(
            new(Context(continued.HostId), new CustomCommandSourceConfiguration(new(7))),
            continueCancellation.Token
        );
        await continueEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var continueRunId = await SingleRunIdAsync(continued);
        continueCancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => continueDispatch);

        (
            await continued.NewRuntime().ResumeAsync(new(continueRunId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Completed);
        continueChat.Messages.ShouldBe(["continue interrupted", "after restart"]);
        var summary = (
            await continued.Queries.ListAsync(new(continued.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary
            .Nodes.Single(node => node.NodeId == continueAction.Id)
            .State.ShouldBe(AutomationNodeRunState.ContinuedAfterFailure);
        summary
            .Nodes.Single(node => node.NodeId == after.Id)
            .State.ShouldBe(AutomationNodeRunState.Succeeded);
    }

    [Test]
    public async Task Restart_WithInterruptedStopAndContinueBranches_StopRemainsTerminal()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var stopDelay = Node("delay", """{"duration-milliseconds":1000}""");
        var continueDelay = Node("delay", """{"duration-milliseconds":1000}""");
        var stopAction = Node("send-chat", """{"message":"must not retry"}""");
        var continueAction = Node(
            "send-chat",
            """{"message":"must not continue"}""",
            AutomationNodeFailurePolicy.Continue
        );
        _ = await fixture.SaveAsync(
            [source, stopDelay, continueDelay, stopAction, continueAction],
            [
                Edge(source, "flow", stopDelay),
                Edge(source, "flow", continueDelay),
                Edge(stopDelay, "complete", stopAction),
                Edge(continueDelay, "complete", continueAction),
            ]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            _ = await db
                .AutomationNodeRuns.Where(node =>
                    node.RunId == runId.Value
                    && (
                        node.NodeId == stopAction.Id.Value || node.NodeId == continueAction.Id.Value
                    )
                )
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(static node => node.Status, AutomationNodeRunStatus.Running)
                        .SetProperty(
                            static node => node.StartedAtUtc,
                            fixture.Clock.GetUtcNow().UtcDateTime
                        )
                );
            _ = await db
                .AutomationFlowRuns.Where(run => run.Id == runId.Value)
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(static run => run.Status, AutomationFlowRunStatus.Running)
                        .SetProperty(static run => run.ExecutionLeaseId, Guid.NewGuid())
                );
        }

        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Failed
        );

        fixture.Chat.Messages.ShouldBeEmpty();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.State.ShouldBe(AutomationFlowRunState.Failed);
        summary
            .Nodes.Single(node => node.NodeId == stopAction.Id)
            .OutcomeCode.ShouldBe("execution-interrupted");
        summary
            .Nodes.Single(node => node.NodeId == continueAction.Id)
            .OutcomeCode.ShouldBe("flow-stopped");
    }

    [Test]
    public async Task Disable_InvalidatesPendingWorkAndReenableNeverReplaysIt()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not replay"}""");
        var flowId = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        var blocked = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        blocked.Status.ShouldBe(AutomationDispatchStatus.FeatureDisabled);
        _ = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.FeatureDisabled>();

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Invalidated);
        fixture.Chat.Messages.ShouldBeEmpty();
        var query = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        query.Runs.ShouldHaveSingleItem().State.ShouldBe(AutomationFlowRunState.Invalidated);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.SingleAsync()).Id.ShouldBe(flowId.Value);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task SecondaryFeatureDisable_InvalidatesAffectedWorkButRetainsAuthorisedHistory()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not replay"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );
        var blocked = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        blocked.Status.ShouldBe(AutomationDispatchStatus.FeatureDisabled);
        var retained = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        retained.Runs.ShouldHaveSingleItem().State.ShouldBe(AutomationFlowRunState.Invalidated);
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Invalidated
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task SensitiveActionExpression_IsEvaluatedButBlockedBeforePublicOutputAndOutcome()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node(
            "send-chat",
            """{"message":"fallback"}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("message"),
                new(AutomationExpressionLanguage.CurrentVersion, "arguments[0]")
            )
        );
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, "do-not-expose"),
                new CustomCommandSourceConfiguration(new(7))
            ),
            CancellationToken.None
        );

        fixture.Chat.Messages.ShouldBeEmpty();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.State.ShouldBe(AutomationFlowRunState.Failed);
        summary.Nodes.ShouldContain(static node => node.OutcomeCode == "sensitive-output-blocked");
        JsonSerializer.Serialize(summary).ShouldNotContain("do-not-expose");
        (
            await fixture.Runtime.ResumeAsync(
                dispatched.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Failed);
    }

    [Test]
    public async Task OverlayCue_ExplicitTargetPersistsAndAdmitsExactlyThatTarget()
    {
        var overlays = new HostBoundOverlayCues();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var target = Guid.NewGuid();
        var otherTargets = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var cue = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, target, OverlayType.CuePlayer);
        foreach (var other in otherTargets)
        {
            overlays.AddTarget(fixture.HostId, other, OverlayType.CuePlayer);
        }
        overlays.AddCue(fixture.HostId, cue, OverlayCueQueuePolicy.Replace);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("target-id"),
                new(AutomationExpressionLanguage.CurrentVersion, "target_id")
            )
        );
        var flowId = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var outcome = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, targetId: target),
                new CustomCommandSourceConfiguration(new(7))
            ),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        overlays.Admissions.ShouldHaveSingleItem().TargetOverlayId.ShouldBe(target);
        overlays.Admissions.ShouldHaveSingleItem().CueId.ShouldBe(cue);
        overlays.Admissions.ShouldNotContain(request =>
            otherTargets.Contains(request.TargetOverlayId)
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        var persisted = await db.AutomationFlowNodes.SingleAsync(node =>
            node.FlowId == flowId.Value && node.DefinitionId == "play-overlay-cue"
        );
        persisted.ConfigurationJson.ShouldContain(target.ToString());
        persisted.ConfigurationJson.ShouldContain(cue.ToString());
    }

    [Test]
    public async Task OverlayCue_UnavailableCrossHostOrWrongTypeTargetsFailClosedBeforeAdmission()
    {
        var overlays = new HostBoundOverlayCues();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var otherHost = await fixture.SeedHostAsync(
            "other-overlay-host",
            HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var validTarget = Guid.NewGuid();
        var otherValidTarget = Guid.NewGuid();
        var wrongTypeTarget = Guid.NewGuid();
        var otherHostTarget = Guid.NewGuid();
        var cue = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, validTarget, OverlayType.CuePlayer);
        overlays.AddTarget(fixture.HostId, otherValidTarget, OverlayType.CuePlayer);
        overlays.AddTarget(fixture.HostId, wrongTypeTarget, OverlayType.Giveaway);
        overlays.AddTarget(otherHost, otherHostTarget, OverlayType.CuePlayer);
        overlays.AddCue(fixture.HostId, cue, OverlayCueQueuePolicy.Replace);

        foreach (var target in new[] { wrongTypeTarget, otherHostTarget })
        {
            var source = Node("custom-command", """{"custom-command-id":7}""");
            var action = Node(
                "play-overlay-cue",
                $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}"""
            );

            var outcome = await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
                CancellationToken.None
            );

            outcome
                .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
                .Errors.ShouldContain(static error =>
                    error.Code == "overlay-reference-unavailable"
                );
        }

        var runtimeSource = Node("custom-command", """{"custom-command-id":7}""");
        var runtimeAction = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{validTarget}}","cue-id":"{{cue}}"}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("target-id"),
                new(AutomationExpressionLanguage.CurrentVersion, "target_id")
            )
        );
        _ = await fixture.SaveAsync(
            [runtimeSource, runtimeAction],
            [Edge(runtimeSource, "flow", runtimeAction)]
        );
        foreach (var target in new[] { wrongTypeTarget, otherHostTarget })
        {
            var dispatched = await fixture.Runtime.DispatchAsync(
                new(
                    Context(fixture.HostId, targetId: target),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            );
            dispatched.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        }

        overlays.Admissions.ShouldBeEmpty();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(1);
        (
            await db.AutomationFlowRuns.CountAsync(static run =>
                run.Status == AutomationFlowRunStatus.Failed
            )
        ).ShouldBe(2);
    }

    [Test]
    public async Task RuntimeAndQueries_AreHostIsolated()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var otherHost = await fixture.SeedHostAsync(
            "other",
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"host one"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var outcome = await fixture.Runtime.DispatchAsync(
            new(Context(otherHost), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.NoMatchingFlow);
        fixture.Chat.Messages.ShouldBeEmpty();
        (await fixture.Queries.ListAsync(new(otherHost), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldBeEmpty();
    }

    private static RecordingChatSender InterruptFirstSend(TaskCompletionSource entered) =>
        new(
            null,
            async (call, _, cancellationToken) =>
            {
                if (call != 1)
                {
                    return;
                }

                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        );

    private static async Task<Guid> SingleRunIdAsync(RuntimeFixture fixture)
    {
        await using var db = await fixture.Database.CreateDbContextAsync();
        return await db.AutomationFlowRuns.Select(static run => run.Id).SingleAsync();
    }

    private static AutomationFlowDraft Draft(
        int hostId,
        ImmutableArray<AutomationFlowDraftNode> nodes,
        ImmutableArray<AutomationFlowDraftEdge> edges
    ) => new(null, new(hostId), "Flow", 1, true, nodes, edges);

    private static AutomationFlowDraftNode Node(
        string type,
        string json,
        AutomationNodeFailurePolicy policy = AutomationNodeFailurePolicy.Stop,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource>? fields =
            null
    )
    {
        using var document = JsonDocument.Parse(json);
        return new(
            new(Guid.NewGuid()),
            new(type, 1, document.RootElement.Clone()),
            AutomationExpressionLanguage.CurrentVersion,
            policy,
            fields
                ?? ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationExpressionSource
                >.Empty
        );
    }

    private static PersistedAutomationNodeDefinition Persisted(string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(type, 1, document.RootElement.Clone());
    }

    private static AutomationFlowDraftEdge Edge(
        AutomationFlowDraftNode source,
        string sourcePort,
        AutomationFlowDraftNode target,
        string targetPort = "flow"
    ) => new(Guid.NewGuid(), source.Id, new(sourcePort), target.Id, new(targetPort));

    private static AutomationContext Context(
        int hostId,
        string argument = "yes",
        string sensitive = "private",
        Guid? targetId = null
    ) =>
        new(
            new(Guid.NewGuid(), AutomationDefinitionIds.CustomCommandSource),
            new("viewer-id", "viewer", "Viewer"),
            new(new(hostId), $"channel-{hostId}", $"host-{hostId}", $"Host {hostId}"),
            null,
            new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            [new(0, argument)],
            new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("private_value")] = new(
                        new AutomationValue.Text(sensitive),
                        AutomationDataSensitivity.Sensitive
                    ),
                    [new("target_id")] = new(
                        new AutomationValue.Text((targetId ?? Guid.Empty).ToString()),
                        AutomationDataSensitivity.Safe
                    ),
                }
            )
        );

    private sealed class RuntimeFixture : IAsyncDisposable
    {
        private RuntimeFixture(
            SqliteBlokeBotDbFactory database,
            MutableTimeProvider clock,
            RecordingChatSender chat,
            HostFeatureService features,
            AutomationCatalogService catalog,
            AutomationExpressionService expressions,
            AutomationActionExecutor actions,
            AutomationRuntimeService runtime,
            AutomationFlowService flows,
            AutomationRunQueryService queries,
            int hostId
        )
        {
            Database = database;
            Clock = clock;
            Chat = chat;
            Features = features;
            Catalog = catalog;
            Expressions = expressions;
            Actions = actions;
            Runtime = runtime;
            Flows = flows;
            Queries = queries;
            HostId = hostId;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal MutableTimeProvider Clock { get; }
        internal RecordingChatSender Chat { get; }
        internal HostFeatureService Features { get; }
        internal AutomationCatalogService Catalog { get; }
        internal AutomationExpressionService Expressions { get; }
        internal AutomationActionExecutor Actions { get; }
        internal AutomationRuntimeService Runtime { get; }
        internal AutomationFlowService Flows { get; }
        internal AutomationRunQueryService Queries { get; }
        internal int HostId { get; }

        internal static async Task<RuntimeFixture> CreateAsync(
            IEnumerable<bool>? chatAdmissions = null,
            IOverlayCueAdmissionService? overlays = null,
            HostFeatureFlags hostFeatures =
                HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands,
            RecordingChatSender? chat = null
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var clock = new MutableTimeProvider(
                new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
            );
            chat ??= new RecordingChatSender(chatAdmissions);
            var observer = new AutomationFeatureDisableObserver(database, clock);
            var features = new HostFeatureService(
                database,
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                [],
                [observer]
            );
            var catalog = new AutomationCatalogService(
                new([new CoreAutomationCatalogModule()]),
                features
            );
            var expressions = new AutomationExpressionService();
            overlays ??= new NoOverlayCues();
            var actions = new AutomationActionExecutor(features, chat, overlays, expressions);
            var runtime = new AutomationRuntimeService(
                database,
                catalog,
                expressions,
                actions,
                clock
            );
            var flows = new AutomationFlowService(database, catalog, expressions, overlays, clock);
            var queries = new AutomationRunQueryService(database, features);
            var fixture = new RuntimeFixture(
                database,
                clock,
                chat,
                features,
                catalog,
                expressions,
                actions,
                runtime,
                flows,
                queries,
                0
            );
            var hostId = await fixture.SeedHostAsync("streamer", hostFeatures);
            return new RuntimeFixture(
                database,
                clock,
                chat,
                features,
                catalog,
                expressions,
                actions,
                runtime,
                flows,
                queries,
                hostId
            );
        }

        internal AutomationRuntimeService NewRuntime() =>
            new(Database, Catalog, Expressions, Actions, Clock);

        internal async Task<AutomationFlowId> SaveAsync(
            ImmutableArray<AutomationFlowDraftNode> nodes,
            ImmutableArray<AutomationFlowDraftEdge> edges
        ) =>
            (await Flows.SaveAsync(Draft(HostId, nodes, edges), CancellationToken.None))
                .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
                .FlowId;

        internal async Task<int> SeedHostAsync(string login, HostFeatureFlags enabledFeatures)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                EnabledFeatures = enabledFeatures,
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            return host.Id;
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private sealed class RecordingChatSender(
        IEnumerable<bool>? admissions,
        Func<int, string, CancellationToken, ValueTask>? beforeSend = null
    ) : IPublicChatMessageSender
    {
        private readonly Queue<bool> _admissions = new(admissions ?? []);
        private int _calls;

        internal ConcurrentQueue<string> Messages { get; } = [];

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Enqueue(message);
            var call = Interlocked.Increment(ref _calls);
            if (beforeSend is not null)
            {
                await beforeSend(call, message, cancellationToken);
            }

            return _admissions.TryDequeue(out var accepted) && !accepted
                ? new PublicChatSendOutcome.Rejected()
                : new PublicChatSendOutcome.Accepted();
        }
    }

    private sealed class NoOverlayCues : IOverlayCueAdmissionService
    {
        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<OverlayCueReferenceOutcome>(
                new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue)
            );

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }

    private sealed class HostBoundOverlayCues : IOverlayCueAdmissionService
    {
        private readonly List<OverlayTargetFixture> _targets = [];
        private readonly List<OverlayCueFixture> _cues = [];

        internal List<OverlayCueAdmissionRequest> Admissions { get; } = [];

        internal void AddTarget(int hostId, Guid id, OverlayType type) =>
            _targets.Add(new(hostId, id, type));

        internal void AddCue(int hostId, Guid id, OverlayCueQueuePolicy queuePolicy) =>
            _cues.Add(new(hostId, id, queuePolicy));

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult(ResolveReferences(request));

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new OverlayCueAdmissionCatalog(
                    _targets
                        .Where(target =>
                            target.HostId == hostId && target.Type == OverlayType.CuePlayer
                        )
                        .Select(static target => new OverlayCueTargetChoice(
                            target.Id,
                            target.Id.ToString()
                        ))
                        .ToImmutableArray(),
                    _cues
                        .Where(cue => cue.HostId == hostId)
                        .Select(static cue => new OverlayCueChoice(
                            cue.Id,
                            cue.Id.ToString(),
                            cue.QueuePolicy
                        ))
                        .ToImmutableArray()
                )
            );

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                ResolveReferences(new(request.HostId, request.TargetOverlayId, request.CueId))
                is OverlayCueReferenceOutcome.Available
                    ? Record(request)
                    : new OverlayCueAdmissionOutcome.Missing()
            );

        private OverlayCueReferenceOutcome ResolveReferences(OverlayCueReferenceRequest request)
        {
            var target = _targets.SingleOrDefault(candidate =>
                candidate.HostId == request.HostId && candidate.Id == request.TargetOverlayId
            );
            return target switch
            {
                null or { Type: not OverlayType.CuePlayer } =>
                    new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Target),
                _ when _cues.Any(candidate =>
                        candidate.HostId == request.HostId && candidate.Id == request.CueId
                    ) => new OverlayCueReferenceOutcome.Available(),
                _ => new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue),
            };
        }

        private OverlayCueAdmissionOutcome Record(OverlayCueAdmissionRequest request)
        {
            Admissions.Add(request);
            return new OverlayCueAdmissionOutcome.Running(Guid.NewGuid());
        }

        private sealed record OverlayTargetFixture(int HostId, Guid Id, OverlayType Type);

        private sealed record OverlayCueFixture(
            int HostId,
            Guid Id,
            OverlayCueQueuePolicy QueuePolicy
        );
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan duration) => now += duration;
    }
}
