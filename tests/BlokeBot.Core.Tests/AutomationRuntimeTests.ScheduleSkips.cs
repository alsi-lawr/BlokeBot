using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    private const string _skipSchedule =
        """{"kind":"Interval","zone":"UTC","local-time":"2026-08-03T12:00:00","day":"Monday","interval-seconds":10,"live-only":"true"}""";

    [Test]
    [Arguments(AutomationScheduleSkipReason.Disabled)]
    [Arguments(AutomationScheduleSkipReason.FeatureDisabled)]
    [Arguments(AutomationScheduleSkipReason.NotReady)]
    [Arguments(AutomationScheduleSkipReason.Offline)]
    [Arguments(AutomationScheduleSkipReason.StreamStatusUnknown)]
    public async Task ScheduleSkips_ObservedGateKeepsUtcIdentityAndNeverReplays(
        AutomationScheduleSkipReason reason
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var flow = await SaveExpandedChatAsync(f, "scheduled-time", _skipSchedule, "future-only");
        var x = ExpandedFixtureFor(f);
        var features = TestHostFeatureServices.Create(
            f.Database,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [x.Runtime],
            f.Clock
        );
        if (reason == AutomationScheduleSkipReason.FeatureDisabled)
        {
            _ = await features.DisableAsync(f.HostId, HostFeatureFlags.Automations, default);
        }
        if (reason == AutomationScheduleSkipReason.Disabled)
        {
            _ = await f.Flows.SetEnabledAsync(new(f.HostId), flow, false, default);
        }
        if (reason == AutomationScheduleSkipReason.NotReady)
        {
            x.Connection.Current = new BotRuntimeStatus.Authorized();
        }
        x.Streams.Current =
            reason == AutomationScheduleSkipReason.StreamStatusUnknown
                ? new HostStreamLivenessOutcome.Unavailable(
                    HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                    new HttpRequestException("private-error-sentinel")
                )
                : new HostStreamLivenessOutcome.Offline();
        await x.Runtime.TickAsync(default);
        var scheduled = f.Clock.GetUtcNow().AddSeconds(10);
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(default);
        f.Chat.Messages.ShouldBeEmpty();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var fact = await db.AutomationScheduleSkips.SingleAsync();
            fact.FlowId.ShouldBe(flow.Value);
            fact.HostId.ShouldBe(f.HostId);
            fact.SourceNodeId.ShouldBe(
                await db
                    .AutomationFlowNodes.Where(node =>
                        node.FlowId == flow.Value && node.DefinitionId == "scheduled-time"
                    )
                    .Select(node => node.Id)
                    .SingleAsync()
            );
            fact.Reason.ShouldBe((int)reason);
            fact.ScheduledAtUtc.ShouldBe(scheduled.UtcDateTime);
            fact.ObservedAtUtc.ShouldBe(scheduled.UtcDateTime);
            fact.ExpiresAtUtc.ShouldBe(scheduled.AddHours(24).UtcDateTime);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationTraces.CountAsync()).ShouldBe(0);
        }
        if (reason == AutomationScheduleSkipReason.Disabled)
        {
            var review = (
                await f.Flows.SetEnabledAsync(new(f.HostId), flow, true, default)
            ).ShouldBeOfType<AutomationFlowEnableOutcome.Review>();
            _ = (
                await f.Flows.SetEnabledAsync(new(f.HostId), flow, true, default, review.Reviewed)
            ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        }
        if (reason == AutomationScheduleSkipReason.FeatureDisabled)
        {
            _ = await features.EnableAsync(f.HostId, HostFeatureFlags.Automations, default);
        }
        x.Connection.Current = new BotRuntimeStatus.Connected(["streamer"]);
        x.Streams.Current = new HostStreamLivenessOutcome.Live("stream", scheduled.AddMinutes(-1));
        await x.Runtime.TickAsync(default);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(default);
        f.Chat.Messages.ShouldBe(["future-only"]);
        var restarted = ExpandedFixtureFor(f);
        f.Clock.Advance(TimeSpan.FromSeconds(35));
        await restarted.Runtime.TickAsync(default);
        f.Chat.Calls.ShouldBe(1);
        await using var verify = await f.Database.CreateDbContextAsync();
        (await verify.AutomationScheduleSkips.CountAsync()).ShouldBe(1);
    }

    [Test]
    [Arguments(2000, false)]
    [Arguments(2001, true)]
    public async Task ScheduleSkips_TwoSecondBoundaryAdvancesOneKnownOccurrenceNotEveryMissedSlot(
        int milliseconds,
        bool late
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "scheduled-time",
            _skipSchedule.Replace("true", "false"),
            "on-time"
        );
        var x = ExpandedFixtureFor(f);
        var now = f.Clock.GetUtcNow();
        await x.Runtime.TickAsync(default);
        f.Clock.Advance(TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(milliseconds));
        await x.Runtime.TickAsync(default);
        f.Chat.Calls.ShouldBe(late ? 0 : 1);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationScheduleSkips.CountAsync()).ShouldBe(late ? 1 : 0);
        if (late)
        {
            var fact = await db.AutomationScheduleSkips.SingleAsync();
            fact.Reason.ShouldBe((int)AutomationScheduleSkipReason.Late);
            fact.ScheduledAtUtc.ShouldBe(now.AddSeconds(10).UtcDateTime);
        }
        f.Clock.Advance(TimeSpan.FromSeconds(35));
        await x.Runtime.TickAsync(default);
        (await db.AutomationScheduleSkips.CountAsync()).ShouldBe(late ? 2 : 1);
    }

    [Test]
    public async Task ScheduleSkips_PartialAndAllRejectedAdmissionCommitBeforeEffectsAndDuplicateIsNotSkip()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingChatSender(
            null,
            async (call, message, token) =>
            {
                if (call == 2)
                {
                    _ = entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
            }
        );
        await using var f = await RuntimeFixture.CreateAsync(chat: sender);
        var json = _skipSchedule.Replace("true", "false");
        var source = Node("scheduled-time", json);
        var wait = Node("delay", """{"duration-milliseconds":60000}""");
        var later = Node("send-chat", """{"message":"later"}""");
        var blocked = await f.SaveAsync(
            [source, wait, later],
            [Edge(source, "flow", wait), Edge(wait, "complete", later)]
        );
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), blocked, new(1, null), default);
        var healthy = await SaveExpandedChatAsync(f, "scheduled-time", json, "healthy");
        var x = ExpandedFixtureFor(f);
        await x.Runtime.TickAsync(default);
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(default);
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        var tick = x.Runtime.TickAsync(default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = await f.Database.CreateDbContextAsync();
            var fact = await db.AutomationScheduleSkips.SingleAsync();
            fact.FlowId.ShouldBe(blocked.Value);
            fact.SourceNodeId.ShouldBe(source.Id.Value);
            fact.Reason.ShouldBe((int)AutomationScheduleSkipReason.FlowUnfinishedLimit);
        }
        finally
        {
            _ = release.TrySetResult();
            await tick;
        }
        await x.Runtime.TickAsync(default);
        await using var verify = await f.Database.CreateDbContextAsync();
        var context = AutomationRuntimeSerialization
            .RestoreContext(
                AutomationContextSchema.CurrentVersion,
                await verify
                    .AutomationFlowRuns.Where(run => run.FlowId == healthy.Value)
                    .OrderBy(run => run.StartedAtUtc)
                    .Select(run => run.ContextJson)
                    .FirstAsync()
            )
            .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
            .Context;
        await f.Runtime.DispatchObservedAsync(
            new(f.HostId),
            (_, _) =>
                Task.FromResult<System.Collections.Immutable.ImmutableArray<AutomationObservedTrigger>>([
                    new(
                        context,
                        configuration => configuration is ScheduledTimeSourceConfiguration
                    ),
                ]),
            default,
            (_, _, outcome, _) =>
            {
                outcome.Status.ShouldBe(AutomationDispatchStatus.Duplicate);
                outcome.Rejections.ShouldBeEmpty();
                return Task.CompletedTask;
            }
        );
        (await verify.AutomationScheduleSkips.CountAsync()).ShouldBe(1);
        _ = await f.Queries.SaveLimitsAsync(new(f.HostId), null, new(1, null), default);
        f.Clock.Advance(TimeSpan.FromSeconds(10));
        await x.Runtime.TickAsync(default);
        (await verify.AutomationScheduleSkips.CountAsync()).ShouldBe(3);
        f.Chat.Messages.ShouldBe(["healthy", "healthy"]);
    }

    [Test]
    public async Task ScheduleSkips_ScopedReadsExpireAt24HoursAndPhysicalCleanupIsBounded()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var flow = new AutomationFlowId(Guid.NewGuid());
        var source = new AutomationNodeId(Guid.NewGuid());
        var now = f.Clock.GetUtcNow();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            for (var i = 0; i < AutomationScheduleSkipStore.CleanupBatchSize + 2; i++)
            {
                await AutomationScheduleSkipStore.AppendAsync(
                    db,
                    new(f.HostId),
                    flow,
                    source,
                    AutomationScheduleSkipReason.Disabled,
                    now.AddSeconds(-1),
                    now,
                    default
                );
            }
        }
        var store = new AutomationScheduleSkipStore(f.Database, f.Clock);
        (await store.ListAsync(new(f.HostId), flow, source, default)).Length.ShouldBe(
            AutomationScheduleSkipStore.QueryLimit
        );
        (await store.ListAsync(new(f.HostId + 1), flow, source, default)).ShouldBeEmpty();
        (
            await store.ListAsync(new(f.HostId), new(Guid.NewGuid()), source, default)
        ).ShouldBeEmpty();
        (await store.ListAsync(new(f.HostId), flow, new(Guid.NewGuid()), default)).ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromTicks(1));
        (await store.ListAsync(new(f.HostId), flow, source, default)).ShouldNotBeEmpty();
        f.Clock.Advance(TimeSpan.FromTicks(1));
        (await store.ListAsync(new(f.HostId), flow, source, default)).ShouldBeEmpty();
        (await store.DeleteExpiredAsync(default)).ShouldBe(
            AutomationScheduleSkipStore.CleanupBatchSize
        );
        (await store.DeleteExpiredAsync(default)).ShouldBe(2);
        (await store.DeleteExpiredAsync(default)).ShouldBe(0);
    }
}
