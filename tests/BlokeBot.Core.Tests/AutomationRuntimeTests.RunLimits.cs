using System.Diagnostics;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Eventing;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task RunLimits_ConcurrentLastSlotDeduplicatesBeforeRejectingAndCountsEachSource()
    {
        var barrier = new AutomationDispatchTransactionInterleaving();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        await LastSlot(f, barrier);
    }

    [Test, Explicit]
    public async Task RunLimits_PostgreSqlConcurrentLastSlotUsesExistingHostLock()
    {
        var barrier = new AutomationDispatchTransactionInterleaving();
        var provider = await PointProviderFixture.PostgreSqlAsync(barrier);
        await using var f = await RuntimeFixture.CreateAsync(provider: provider);
        await LastSlot(f, barrier);
    }

    private static async Task LastSlot(
        RuntimeFixture f,
        AutomationDispatchTransactionInterleaving barrier
    )
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var second = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":60000}""");
        var send = Node("send-chat", """{"message":"later"}""");
        var flow = await f.SaveAsync(
            [source, second, delay, send],
            [
                Edge(source, "flow", delay),
                Edge(second, "flow", delay),
                Edge(delay, "complete", send),
            ]
        );
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), flow, new(1, null), default);
        await f.Runtime.InitializeAsync(default);
        barrier.Arm();
        var held = f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        try
        {
            await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var winningContext = Context(f.HostId);
            var winner = await f.Runtime.DispatchAsync(
                new(winningContext, new CustomCommandSourceConfiguration(new(7))),
                default
            );
            winner.RunIds.Length.ShouldBe(1);
            winner
                .Rejections.ShouldHaveSingleItem()
                .Reason.ShouldBe(AutomationAdmissionRejectionReason.FlowUnfinishedLimit);
            barrier.Release();
            var rejected = await held;
            rejected.Status.ShouldBe(AutomationDispatchStatus.RunLimitReached);
            rejected.RunIds.ShouldBeEmpty();
            rejected.Rejections.Length.ShouldBe(2);
            var repeated = await f.Runtime.DispatchAsync(
                new(winningContext, new CustomCommandSourceConfiguration(new(7))),
                default
            );
            repeated.RunIds.ShouldBeEmpty();
            repeated.Rejections.Length.ShouldBe(1);
            (await f.Queries.ReadLimitsAsync(new(f.HostId), flow, default))
                .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
                .Usage.ShouldBe(new(new(1, null), 1, 1));
            _ = await f.Queries.SaveLimitsAsync(new(f.HostId), flow, new(null, 1), default);
            var waitingLimit = await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            );
            waitingLimit.Rejections.ShouldAllBe(rejection =>
                rejection.Reason == AutomationAdmissionRejectionReason.FlowWaitingLimit
            );
            waitingLimit.Rejections.Length.ShouldBe(2);
            (await f.Runtime.CancelFlowRunsAsync(new(f.HostId), flow, default)).ShouldBe(1);
            f.Chat.Calls.ShouldBe(0);
        }
        finally
        {
            barrier.Release();
            _ = await held;
        }
    }

    [Test]
    public async Task RunLimits_AllRejectedConsumesNoCommandClaimOrCooldownButHealthySiblingClaimsOnce()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":60000}""");
        var send = Node("send-chat", """{"message":"blocked"}""");
        var blocked = await f.SaveAsync(
            [source, delay, send],
            [Edge(source, "flow", delay), Edge(delay, "complete", send)]
        );
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), blocked, new(1, null), default);
        _ = await f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        var cooldown = new CustomCommandCooldownStore(f.Clock);
        var adapter = new CustomCommandAutomationRuntime(f.Runtime, cooldown, new(f.Clock));
        var request = IsolationRequest(f);
        var notice = new List<AutomationAdmissionRejectionReason>();
        using var subscription = f.UiChanges.Subscribe(
            new AutomationUiChange.RunLimitRejected(
                new(f.HostId),
                blocked,
                AutomationAdmissionRejectionReason.FlowUnfinishedLimit
            ),
            ObserverIdentity.For(typeof(AutomationRuntimeTests)),
            (notification, _) =>
            {
                notice.Add(((AutomationUiChange.RunLimitRejected)notification.Key).Reason);
                return ValueTask.CompletedTask;
            }
        );
        var denied = IsolationDispatch(await adapter.DispatchAsync(request, default));
        denied.Status.ShouldBe(AutomationDispatchStatus.RunLimitReached);
        cooldown.EntryCount.ShouldBe(0);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
            (await db.AutomationTraces.CountAsync()).ShouldBe(1);
        }
        notice.ShouldBe([AutomationAdmissionRejectionReason.FlowUnfinishedLimit]);
        _ = await IsolationSaveChat(f, f.Flows, "healthy");
        var accepted = IsolationDispatch(await adapter.DispatchAsync(request, default));
        _ = accepted.RunIds.ShouldHaveSingleItem();
        accepted.Rejections.ShouldHaveSingleItem().FlowId.ShouldBe(blocked);
        f.Chat.Messages.ShouldBe(["healthy"]);
        cooldown.EntryCount.ShouldBe(1);
        _ = (
            await adapter.DispatchAsync(request, default)
        ).ShouldBeOfType<CustomCommandAutomationDispatchOutcome.Cooldown>();
        await using var verify = await f.Database.CreateDbContextAsync();
        (await verify.CustomCommandInvocationClaims.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task RunLimits_BurstWaitingThresholdPreservesAdmittedWorkAndReleasesCapacityAcrossRestart()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":60000}""");
        var send = Node("send-chat", """{"message":"finished"}""");
        var flow = await f.SaveAsync(
            [source, delay, send],
            [Edge(source, "flow", delay), Edge(delay, "complete", send)]
        );
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), null, new(3, 2), default);
        var accepted = new List<AutomationRunId>();
        var rejected = 0;
        var max = 0;
        var elapsed = Stopwatch.StartNew();
        for (var index = 0; index < 12; index++)
        {
            var dispatch = await f.Runtime.DispatchExpandedAsync(
                Context(f.HostId),
                configuration => configuration is CustomCommandSourceConfiguration,
                default,
                deferExecution: true
            );
            accepted.AddRange(dispatch.RunIds);
            if (dispatch.Status == AutomationDispatchStatus.RunLimitReached)
            {
                rejected++;
            }
            var usage = (await f.Queries.ReadLimitsAsync(new(f.HostId), null, default))
                .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
                .Usage;
            max = Math.Max(max, usage.Unfinished);
        }
        accepted.Count.ShouldBe(3);
        rejected.ShouldBe(9);
        max.ShouldBe(3);
        foreach (var run in accepted)
        {
            (await f.Runtime.ResumeAsync(run, default)).Status.ShouldBe(
                AutomationResumeStatus.Waiting
            );
        }
        (await f.Queries.ReadLimitsAsync(new(f.HostId), null, default))
            .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
            .Usage.Waiting.ShouldBe(3);
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), null, new(null, 2), default);
        var waitingReject = await f.Runtime.DispatchAsync(
            new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
            default
        );
        waitingReject
            .Rejections.ShouldHaveSingleItem()
            .Reason.ShouldBe(AutomationAdmissionRejectionReason.ChannelWaitingLimit);
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), flow, new(1, 1), default);
        (await f.Runtime.CancelRunAsync(new(f.HostId), accepted[0], default)).ShouldBe(1);
        var restarted = f.NewRuntime();
        await restarted.InitializeAsync(default);
        (await f.Queries.ReadLimitsAsync(new(f.HostId), null, default))
            .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
            .Usage.Unfinished.ShouldBe(2);
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        await restarted.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["finished", "finished"]);
        (await f.Queries.ReadLimitsAsync(new(f.HostId), null, default))
            .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
            .Usage.ShouldBe(new(new(null, 2), 0, 0));
        Console.WriteLine(
            $"RunLimits workload: SQLite; 12 starts, accepted={accepted.Count}, rejected={rejected}, maximum unfinished={max}, configured unfinished=3; 3 later waits above threshold2; cancelled1/resumed2/restart1; elapsed={elapsed.ElapsedMilliseconds}ms; fake wait=60000ms."
        );
    }

    [Test]
    public async Task RunLimits_ClaimedDueWorkIsRunningWhileItsEffectIsHeld()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(
            null,
            async (_, _, token) =>
            {
                _ = entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        );
        await using var f = await RuntimeFixture.CreateAsync(chat: chat);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var send = Node("send-chat", """{"message":"held"}""");
        _ = await f.SaveAsync(
            [source, delay, send],
            [Edge(source, "flow", delay), Edge(delay, "complete", send)]
        );
        var run = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        var resume = f.Runtime.ResumeAsync(run, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await f.Queries.ReadLimitsAsync(new(f.HostId), null, default))
                .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
                .Usage.ShouldBe(new(new(null, null), 1, 0));
            _ = await f.Runtime.CancelRunAsync(new(f.HostId), run, default);
        }
        finally
        {
            _ = release.TrySetResult();
            _ = await resume;
        }
        (await f.Queries.ReadLimitsAsync(new(f.HostId), null, default))
            .ShouldBeOfType<AutomationRunLimitsOutcome.Available>()
            .Usage.Unfinished.ShouldBe(0);
    }
}
