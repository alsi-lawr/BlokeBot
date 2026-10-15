using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task AdmissionIsolation_TwoHealthyFlowsUseOneClaimAndCooldown_WithoutReplay()
    {
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(executionChat: sender);
        var a = await IsolationSaveChat(f, f.Flows, "valid-a");
        var b = await IsolationSaveChat(f, f.Flows, "valid-b");
        var cooldown = new CustomCommandCooldownStore(f.Clock);
        var adapter = new CustomCommandAutomationRuntime(f.Runtime, cooldown, new(f.Clock));
        var request = IsolationRequest(f);
        var noMatch = IsolationDispatch(
            await adapter.DispatchAsync(
                request with
                {
                    CommandId = 8,
                    Trigger = request.Trigger with
                    {
                        SourceConfiguration = new CustomCommandSourceConfiguration(new(8)),
                    },
                    InvocationClaim = new(
                        f.HostId,
                        8,
                        new CustomCommandInvocationScope.OncePerUser("viewer-id")
                    ),
                },
                default
            )
        );
        noMatch.Status.ShouldBe(AutomationDispatchStatus.NoMatchingFlow);
        noMatch.RunIds.ShouldBeEmpty();
        noMatch.Rejections.ShouldBeEmpty();
        cooldown.EntryCount.ShouldBe(0);
        await using (var empty = await f.Database.CreateDbContextAsync())
        {
            (await empty.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        }
        var result = IsolationDispatch(await adapter.DispatchAsync(request, default));
        result.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        result.RunIds.Length.ShouldBe(2);
        result.Rejections.ShouldBeEmpty();
        await IsolationAssertRuns(f, [a, b], result.RunIds.Select(id => id.Value).ToArray());
        sender.Accepted.Order().ShouldBe(["valid-a", "valid-b"]);
        cooldown.EntryCount.ShouldBe(1);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var claim = await db.CustomCommandInvocationClaims.SingleAsync();
            claim.HostId.ShouldBe(f.HostId);
            claim.CustomCommandId.ShouldBe(7);
            claim.TwitchUserId.ShouldBe("viewer-id");
        }
        _ = (
            await adapter.DispatchAsync(request, default)
        ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.Cooldown>();
        f.Clock.Advance(TimeSpan.FromSeconds(31));
        IsolationDispatch(await adapter.DispatchAsync(request, default))
            .Status.ShouldBe(AutomationDispatchStatus.Duplicate);
        cooldown.EntryCount.ShouldBe(0);
        _ = (
            await adapter.DispatchAsync(
                request with
                {
                    Trigger = request.Trigger with { Context = Context(f.HostId) },
                },
                default
            )
        ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.AlreadyUsed>();
        sender.Entries.Count.ShouldBe(2);
    }

    [Test]
    [Arguments(8)]
    [Arguments(7)]
    public async Task AdmissionIsolation_OnlyMatchingDegradedClosureIsRejected_HealthySiblingKeepsItsRun(
        int nestedCommand
    )
    {
        var logger = new IsolationLogger();
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(executionChat: sender, logger: logger);
        var p = await SubflowPlugin(f, required: true);
        var healthy = await IsolationSaveChat(f, p.Flows, "healthy");
        var bad = await IsolationNested(f, p, nestedCommand);
        (await p.Flows.ValidateAsync(bad.Draft, default)).Errors.ShouldBeEmpty();
        IsolationDegrade(p);
        var cooldown = new CustomCommandCooldownStore(f.Clock);
        var adapter = new CustomCommandAutomationRuntime(p.Runtime, cooldown, new(f.Clock));
        var request = IsolationRequest(f);
        var result = IsolationDispatch(await adapter.DispatchAsync(request, default));
        result.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        await IsolationAssertRuns(f, [healthy], result.RunIds.Select(id => id.Value).ToArray());
        sender.Accepted.ShouldBe(["healthy"]);
        p.Invoker.Calls.ShouldBe(0);
        cooldown.EntryCount.ShouldBe(1);
        if (nestedCommand == 7)
        {
            var rejection = result.Rejections.ShouldHaveSingleItem();
            rejection.FlowId.ShouldBe(bad.FlowId);
            rejection.SourceNodeIds.ShouldBe([bad.Source]);
            rejection.Reason.ShouldBe(AutomationAdmissionRejectionReason.ExecutionUnavailable);
            var fields = logger.Events.ShouldHaveSingleItem();
            fields["HostId"].ShouldBe(f.HostId);
            fields["FlowId"].ShouldBe(bad.FlowId.Value);
            fields["SourceNodeId"].ShouldBe(bad.Source.Value);
            fields["Reason"].ShouldBe(rejection.Reason);
            JsonSerializer.Serialize(logger.Events).ShouldNotContain("private");
            JsonSerializer.Serialize(result).ShouldNotContain("private");
            IsolationRepair(p);
            f.Clock.Advance(TimeSpan.FromSeconds(31));
            _ = (
                await adapter.DispatchAsync(request, default)
            ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.AlreadyUsed>();
            _ = (
                await adapter.DispatchAsync(
                    request with
                    {
                        Trigger = request.Trigger with { Context = Context(f.HostId) },
                    },
                    default
                )
            ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.AlreadyUsed>();
            sender.Entries.Count.ShouldBe(1);
            p.Invoker.Calls.ShouldBe(0);
        }
        else
        {
            result.Rejections.ShouldBeEmpty();
            logger.Events.ShouldBeEmpty();
        }
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmissionIsolation_BadFlowBeforeOrAfterHealthyFlowNeverStagesRejectedPrefix(
        bool badFirst
    )
    {
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(executionChat: sender);
        var p = await SubflowPlugin(f, required: true);
        _ = await IsolationSaveChat(f, p.Flows, "healthy");
        var nested = await IsolationNested(f, p, 7);
        Guid[] order;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            order = await db
                .AutomationFlows.OrderBy(flow => flow.Id)
                .Select(flow => flow.Id)
                .ToArrayAsync();
        }
        var badId = new AutomationFlowId(order[badFirst ? 0 : 1]);
        var healthyId = new AutomationFlowId(order[badFirst ? 1 : 0]);
        // Release the original nested node IDs before assigning its graph to the selected saved identity.
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"healthy"}""");
        _ = (
            await p.Flows.SaveAsync(
                Draft(f.HostId, [source, action], [Edge(source, "flow", action)]) with
                {
                    Id = nested.FlowId,
                },
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        if (healthyId != nested.FlowId)
        {
            var s = Node("custom-command", """{"custom-command-id":7}""");
            var a = Node("send-chat", """{"message":"healthy"}""");
            _ = (
                await p.Flows.SaveAsync(
                    Draft(f.HostId, [s, a], [Edge(s, "flow", a)]) with
                    {
                        Id = healthyId,
                    },
                    default
                )
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        }
        _ = (
            await p.Flows.SaveAsync(nested.Draft with { Id = badId }, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        IsolationDegrade(p);
        var result = await p.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        result.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        result.Rejections.ShouldHaveSingleItem().FlowId.ShouldBe(badId);
        await IsolationAssertRuns(f, [healthyId], result.RunIds.Select(id => id.Value).ToArray());
        sender.Accepted.ShouldBe(["healthy"]);
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        var traces = await final
            .AutomationTraceEvents.Select(trace => trace.EventJson)
            .ToArrayAsync();
        string.Join("", traces).ShouldNotContain(nested.Source.Value.ToString());
    }

    [Test]
    public async Task AdmissionIsolation_AllRejectedConsumesNeitherClaimNorCooldown_ExactRetryAfterRepairWorksOnce()
    {
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(executionChat: sender);
        var p = await SubflowPlugin(f, required: true);
        var bad = await IsolationNested(f, p, 7);
        IsolationDegrade(p);
        var cooldown = new CustomCommandCooldownStore(f.Clock);
        var adapter = new CustomCommandAutomationRuntime(p.Runtime, cooldown, new(f.Clock));
        var request = IsolationRequest(f);
        var rejected = IsolationDispatch(await adapter.DispatchAsync(request, default));
        rejected.Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        rejected.RunIds.ShouldBeEmpty();
        rejected.Rejections.ShouldHaveSingleItem().FlowId.ShouldBe(bad.FlowId);
        cooldown.EntryCount.ShouldBe(0);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationTraceEvents.CountAsync()).ShouldBe(0);
            (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
            (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        }
        IsolationRepair(p);
        var accepted = IsolationDispatch(await adapter.DispatchAsync(request, default));
        accepted.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        accepted.Rejections.ShouldBeEmpty();
        p.Invoker.Calls.ShouldBe(1);
        sender.Accepted.ShouldBe(["nested-admitted"]);
        await IsolationAssertRuns(
            f,
            [bad.FlowId],
            accepted.RunIds.Select(id => id.Value).ToArray()
        );
        cooldown.EntryCount.ShouldBe(1);
        _ = (
            await adapter.DispatchAsync(request, default)
        ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.Cooldown>();
        f.Clock.Advance(TimeSpan.FromSeconds(31));
        IsolationDispatch(await adapter.DispatchAsync(request, default))
            .Status.ShouldBe(AutomationDispatchStatus.Duplicate);
        p.Invoker.Calls.ShouldBe(1);
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task AdmissionIsolation_CommittedBatchHeldBeforeAcceptedResult_RedeliveryAndSameFreshDueDoNotReplay()
    {
        var sender = new AdmissionSender();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.BeforeAccept = async () =>
        {
            if (sender.Entries.Count == 1)
            {
                _ = entered.TrySetResult();
                await release.Task;
            }
        };
        await using var f = await RuntimeFixture.CreateAsync(executionChat: sender);
        var a = await IsolationSaveChat(f, f.Flows, "a");
        var b = await IsolationSaveChat(f, f.Flows, "b");
        var cooldown = new CustomCommandCooldownStore(f.Clock);
        var adapter = new CustomCommandAutomationRuntime(f.Runtime, cooldown, new(f.Clock));
        var request = IsolationRequest(f);
        var dispatch = adapter.DispatchAsync(request, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            sender.Accepted.ShouldBeEmpty();
            await using var db = await f.Database.CreateDbContextAsync();
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(2);
            (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
            cooldown.EntryCount.ShouldBe(1);
            _ = (
                await adapter.DispatchAsync(request, default)
            ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.Cooldown>();
            f.Clock.Advance(TimeSpan.FromSeconds(31));
            IsolationDispatch(await adapter.DispatchAsync(request, default))
                .Status.ShouldBe(AutomationDispatchStatus.Duplicate);
        }
        finally
        {
            _ = release.TrySetResult();
        }
        var result = IsolationDispatch(await dispatch.WaitAsync(TimeSpan.FromSeconds(10)));
        await f.Runtime.ResumeDueAsync(default);
        await f.Runtime.ResumeDueAsync(default);
        var fresh = f.NewRuntime();
        await fresh.InitializeAsync(default);
        await fresh.ResumeDueAsync(default);
        await fresh.ResumeDueAsync(default);
        sender.Accepted.Order().ShouldBe(["a", "b"]);
        sender.Entries.Count.ShouldBe(2);
        await IsolationAssertRuns(f, [a, b], result.RunIds.Select(id => id.Value).ToArray());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmissionIsolation_FirstObservedConsumesEligibleCapture_WithRejectedFlowAndNoRepairedReplay(
        bool healthySibling
    )
    {
        var cut = new IsolationDegradeAfterObservation("automation_seen_viewers");
        var sender = new AdmissionSender();
        var logger = new IsolationLogger();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut],
            logger: logger
        );
        var p = await SubflowPlugin(
            f,
            required: true,
            additionalModules: [new ExpandedAutomationCatalogModule()]
        );
        var bad = await IsolationNested(
            f,
            p,
            7,
            "chat-match",
            """{"kind":"FirstObserved","match":""}"""
        );
        if (healthySibling)
        {
            _ = await IsolationExpandedChat(
                f,
                p.Flows,
                "chat-match",
                """{"kind":"FirstObserved","match":""}""",
                "healthy-first"
            );
        }
        var x = await WarmAdmissionChatAsync(f, p.Runtime);
        cut.Arm(() => IsolationDegrade(p));
        var message = AdmissionMessage(f);
        await x.ChatReceivedAsync(message, default);
        cut.Fired.ShouldBe(1);
        _ = cut.TransactionId.ShouldNotBeNull();
        sender.Accepted.ShouldBe(healthySibling ? ["healthy-first"] : []);
        logger.Events.ShouldHaveSingleItem()["FlowId"].ShouldBe(bad.FlowId.Value);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationSeenViewers.CountAsync()).ShouldBe(1);
            (
                await db.AutomationFlowRuns.AnyAsync(run => run.FlowId == bad.FlowId.Value)
            ).ShouldBeFalse();
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(healthySibling ? 1 : 0);
            string.Join(
                    "",
                    await db.AutomationTraceEvents.Select(trace => trace.EventJson).ToArrayAsync()
                )
                .ShouldNotContain(_admissionPrivateMessage);
        }
        IsolationRepair(p);
        await x.ChatReceivedAsync(message, default);
        await x.ChatReceivedAsync(message with { MessageId = "same-viewer-new-message" }, default);
        var fresh = IsolationFreshRuntime(f, p);
        var freshSource = await WarmAdmissionChatAsync(f, fresh);
        await freshSource.ChatReceivedAsync(message, default);
        await fresh.ResumeDueAsync(default);
        p.Invoker.Calls.ShouldBe(0);
        sender.Entries.Count.ShouldBe(healthySibling ? 1 : 0);
    }

    [Test]
    public async Task AdmissionIsolation_GoalLifecycleAndMilestonesSkipRejectedClosures_CommitHealthyBatchAndNoReplay()
    {
        var cut = new IsolationDegradeAfterObservation("automation_goal_observations");
        var sender = new AdmissionSender();
        var logger = new IsolationLogger();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut],
            logger: logger
        );
        var p = await SubflowPlugin(
            f,
            required: true,
            additionalModules: [new ExpandedAutomationCatalogModule()]
        );
        var badLifecycle = await IsolationNested(
            f,
            p,
            7,
            "twitch-goal",
            """{"event":"Progressed","milestone":10}"""
        );
        var badMilestone = await IsolationNested(
            f,
            p,
            7,
            "twitch-goal",
            """{"event":"Milestone","milestone":10}"""
        );
        foreach (
            var (kind, amount, text) in new[]
            {
                ("Progressed", 10, "progress"),
                ("Milestone", 10, "ten"),
                ("Milestone", 20, "twenty"),
            }
        )
        {
            _ = await IsolationExpandedChat(
                f,
                p.Flows,
                "twitch-goal",
                JsonSerializer.Serialize(new { @event = kind, milestone = amount }),
                text
            );
        }
        await p.Runtime.InitializeAsync(default);
        var x = AdmissionExpandedFor(f, p.Runtime);
        await x.GoalChangedAsync(AdmissionGoal(f, EventSubGoalStage.Begin, 8), default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        cut.Arm(() => IsolationDegrade(p));
        sender.BeforeAccept = async () =>
        {
            await using var db = await f.Database.CreateDbContextAsync();
            (await db.AutomationGoalObservations.SingleAsync()).CurrentAmount.ShouldBe(20);
            (await db.AutomationGoalMilestones.CountAsync()).ShouldBe(2);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(3);
            (
                await db.AutomationFlowRuns.AnyAsync(run =>
                    run.FlowId == badLifecycle.FlowId.Value
                    || run.FlowId == badMilestone.FlowId.Value
                )
            ).ShouldBeFalse();
        };
        var crossing = AdmissionGoal(f, EventSubGoalStage.Progress, 20);
        await x.GoalChangedAsync(crossing, default);
        cut.Fired.ShouldBe(1);
        _ = cut.TransactionId.ShouldNotBeNull();
        sender.Accepted.Order().ShouldBe(["progress", "ten", "twenty"]);
        logger
            .Events.Select(fields => (Guid)fields["FlowId"]!)
            .Distinct()
            .Order()
            .ShouldBe(new[] { badLifecycle.FlowId.Value, badMilestone.FlowId.Value }.Order());
        IsolationRepair(p);
        await x.GoalChangedAsync(crossing, default);
        var fresh = IsolationFreshRuntime(f, p);
        await fresh.InitializeAsync(default);
        await fresh.ResumeDueAsync(default);
        await fresh.ResumeDueAsync(default);
        p.Invoker.Calls.ShouldBe(0);
        sender.Entries.Count.ShouldBe(3);
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationGoalMilestones.CountAsync()).ShouldBe(2);
        (await final.AutomationFlowRuns.CountAsync()).ShouldBe(3);
    }

    [Test]
    public async Task AdmissionIsolation_ManualAggregationPreservesFlowAndSourceRejections()
    {
        var logger = new IsolationLogger();
        await using var f = await RuntimeFixture.CreateAsync(logger: logger);
        var p = await SubflowPlugin(
            f,
            required: true,
            additionalModules: [new ExpandedAutomationCatalogModule()]
        );
        var bad = await IsolationNested(
            f,
            p,
            7,
            AutomationDefinitionIds.ManualSource.Value,
            """{"data":"one"}"""
        );
        var second = Node(AutomationDefinitionIds.ManualSource.Value, """{"data":"two"}""");
        var draft = bad.Draft with
        {
            Nodes = [.. bad.Draft.Nodes, second],
            Edges = [.. bad.Draft.Edges, Edge(second, "flow", bad.Draft.Nodes[1])],
        };
        _ = (
            await p.Flows.SaveAsync(draft, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        IsolationDegrade(p);
        var manual = new AutomationManualRunService(f.Database, p.Runtime, p.Catalog, f.Clock);
        var result = (await manual.RunAsync(new(f.HostId), bad.FlowId, default))
            .ShouldBeOfType<AutomationManualRunOutcome.Dispatched>()
            .Dispatch;
        result.Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        result.RunIds.ShouldBeEmpty();
        result.Rejections.Length.ShouldBe(2);
        result.Rejections.ShouldAllBe(rejection => rejection.FlowId == bad.FlowId);
        result
            .Rejections.SelectMany(rejection => rejection.SourceNodeIds)
            .OrderBy(id => id.Value)
            .ShouldBe(new[] { bad.Source, second.Id }.OrderBy(id => id.Value));
        logger.Events.Count.ShouldBe(2);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
    }

    private static AutomationDispatchOutcome IsolationDispatch(
        CustomCommandAutomationDispatchOutcome result
    ) => result.ShouldBeOfType<CustomCommandAutomationDispatchOutcome.Dispatched>().Dispatch;

    private static async Task IsolationAssertRuns(
        RuntimeFixture f,
        AutomationFlowId[] flows,
        Guid[] ids
    )
    {
        await using var db = await f.Database.CreateDbContextAsync();
        var runs = await db.AutomationFlowRuns.Include(run => run.NodeRuns).ToArrayAsync();
        runs.Select(run => run.FlowId).Order().ShouldBe(flows.Select(flow => flow.Value).Order());
        runs.Select(run => run.Id).Order().ShouldBe(ids.Order());
        runs.ShouldAllBe(run =>
            run.Status == AutomationFlowRunStatus.Completed && run.ExecutionLeaseId == null
        );
        runs.ShouldAllBe(run =>
            run.NodeRuns.All(node => node.Status == AutomationNodeRunStatus.Succeeded)
        );
        runs.Select(run => (run.FlowId, run.SourceNodeId, run.SourceOccurrenceId))
            .Distinct()
            .Count()
            .ShouldBe(flows.Length);
    }

    private static AutomationRuntimeService IsolationFreshRuntime(
        RuntimeFixture f,
        SubflowPluginServices p
    )
    {
        var runtime = new AutomationRuntimeService(
            f.Database,
            p.Catalog,
            p.Flows,
            f.Actions,
            f.Clock,
            f.UiChanges,
            f.Logger
        );
        runtime.UsePluginExecution(p.Execution);
        return runtime;
    }

    private static async Task<AutomationFlowId> IsolationExpandedChat(
        RuntimeFixture f,
        AutomationFlowService flows,
        string sourceId,
        string sourceJson,
        string message
    )
    {
        var source = Node(sourceId, sourceJson);
        var action = Node("send-chat", JsonSerializer.Serialize(new { message }));
        return (
            await flows.SaveAsync(
                Draft(f.HostId, [source, action], [Edge(source, "flow", action)]),
                default
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
    }

    private static void IsolationRepair(SubflowPluginServices p)
    {
        PluginFeatureRevision.TryCreate(3, out var revision).ShouldBeTrue();
        p.Snapshots.Publish(p.State with { Revision = revision });
    }

    private static async Task<AutomationFlowId> IsolationSaveChat(
        RuntimeFixture f,
        AutomationFlowService flows,
        string message
    )
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", JsonSerializer.Serialize(new { message }));
        return (
            await flows.SaveAsync(
                Draft(f.HostId, [source, action], [Edge(source, "flow", action)]),
                default
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
    }

    private static async Task<(
        AutomationFlowId FlowId,
        AutomationNodeId Source,
        AutomationFlowDraft Draft
    )> IsolationNested(
        RuntimeFixture f,
        SubflowPluginServices p,
        int command,
        string? sourceId = null,
        string? sourceJson = null
    )
    {
        var leaf = Subflow(f.HostId);
        leaf = leaf with
        {
            Graph = leaf.Graph with
            {
                Nodes = [leaf.Graph.Nodes[0], p.Node, leaf.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leaf.Graph.Nodes[0], "complete", p.Node),
                    Edge(p.Node, "complete", leaf.Graph.Nodes[1]),
                ],
            },
        };
        var revision = await Publish(new(f.Database, p.Flows, f.Clock), leaf);
        var draft = Caller(f.HostId, revision);
        var source = Node(
            sourceId ?? "custom-command",
            sourceJson
                ?? JsonSerializer.Serialize(
                    new Dictionary<string, int> { ["custom-command-id"] = command }
                )
        );
        var before = Node("send-chat", """{"message":"nested-admitted"}""");
        draft = draft with
        {
            Nodes = [source, before, draft.Nodes[1]],
            Edges = [Edge(source, "flow", before), Edge(before, "complete", draft.Nodes[1])],
        };
        var saved = (
            await p.Flows.SaveAsync(draft, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        return (saved.FlowId, source.Id, draft with { Id = saved.FlowId });
    }

    private static void IsolationDegrade(SubflowPluginServices p)
    {
        PluginFeatureRevision.TryCreate(2, out var revision).ShouldBeTrue();
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.MissingScopes,
                PluginRecoveryAction.ReconnectTwitch,
                "fixture",
                out var reason
            )
            .ShouldBeTrue();
        p.Snapshots.Publish(
            p.State with
            {
                Revision = revision,
                Readiness = new PluginFeatureReadiness.EnabledDegraded(reason),
            }
        );
    }

    private static CustomCommandAutomationDispatchRequest IsolationRequest(RuntimeFixture f) =>
        new(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            7,
            CustomCommandCooldownScope.Global,
            "viewer",
            TimeSpan.FromSeconds(30),
            new(f.HostId, 7, new CustomCommandInvocationScope.OncePerUser("viewer-id"))
        );

    private sealed class IsolationLogger : ILogger<AutomationRuntimeService>
    {
        internal ConcurrentQueue<Dictionary<string, object?>> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            exception.ShouldBeNull();
            Events.Enqueue(
                ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value
                )
            );
        }
    }

    private sealed class IsolationDegradeAfterObservation(string table) : DbCommandInterceptor
    {
        private Action? _degrade;
        internal int Fired { get; private set; }
        internal Guid? TransactionId { get; private set; }

        internal void Arm(Action degrade) => _degrade = degrade;

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (
                _degrade is not { } degrade
                || !command.CommandText.Contains(table, StringComparison.Ordinal)
                || !(
                    command.CommandText.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                    || command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return;
            }
            _degrade = null;
            TransactionId = eventData.Context?.Database.CurrentTransaction?.TransactionId;
            Fired++;
            degrade();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }
    }
}
