using System.Text.Json;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task DashboardControls_SelectedIdenticalSource_AdmitsOnlyItsEffects_WholeFlowStillRunsBoth()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var first = Node("manual-run", """{"data":"same"}""");
        var second = Node("manual-run", """{"data":"same"}""");
        var firstAction = Node("send-chat", """{"message":"first"}""");
        var secondAction = Node("send-chat", """{"message":"second"}""");
        var flow = await f.SaveAsync(
            [first, second, firstAction, secondAction],
            [Edge(first, "flow", firstAction), Edge(second, "flow", secondAction)]
        );
        var controls = DashboardControls(f);
        var admitted = DashboardDispatched(
            await controls.RunAsync(
                DashboardSession(f.HostId),
                new(f.HostId),
                DashboardButton(flow, second.Id),
                CancellationToken.None
            )
        );
        admitted.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        admitted.RunIds.Length.ShouldBe(1);
        f.Chat.Messages.ShouldBe(["second"]);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.SingleAsync()).SourceNodeId.ShouldBe(second.Id.Value);
        var whole = await new AutomationManualRunService(
            f.Database,
            f.Runtime,
            f.Catalog,
            f.Clock
        ).RunAsync(new(f.HostId), flow, CancellationToken.None);
        whole
            .ShouldBeOfType<AutomationManualRunOutcome.Dispatched>()
            .Dispatch.RunIds.Length.ShouldBe(2);
        f.Chat.Messages.Skip(1).Order().ShouldBe(["first", "second"]);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(3);
    }

    [Test]
    [Arguments(false, "ignored", "saved")]
    [Arguments(true, "replacement", "replacement")]
    [Arguments(true, "", "")]
    public async Task DashboardControls_ManualOverride_IsInvocationOnly_AndExplicitEmptyIsRetained(
        bool replace,
        string text,
        string expected
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"saved"}""");
        var flow = await f.SaveAsync([source], []);
        var configuration = DashboardButton(flow, source.Id) with
        {
            Data = replace
                ? new DashboardTextOverride.Replace(text)
                : new DashboardTextOverride.Inherit(),
        };
        var dispatched = DashboardDispatched(
            await DashboardControls(f)
                .RunAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    configuration,
                    CancellationToken.None
                )
        );
        _ = dispatched.RunIds.ShouldHaveSingleItem();
        await using var db = await f.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.SingleAsync();
        var context = AutomationRuntimeSerialization
            .RestoreContext(run.ContextSchemaVersion, run.ContextJson)
            .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
            .Context;
        context
            .Variables.ForExecution()[new("manual-data")]
            .Value.ShouldBe(new AutomationValue.Text(expected));
        using var saved = JsonDocument.Parse(
            (await db.AutomationFlowNodes.SingleAsync()).ConfigurationJson
        );
        saved.RootElement.GetProperty("data").GetString().ShouldBe("saved");
    }

    [Test]
    public async Task DashboardControls_RejectedAuthorityAndForeignOrDisabledTargets_MutateNothing()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"saved"}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        var flow = await f.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var other = await f.SeedHostAsync("other", HostFeatureFlags.Automations);
        var moderator = new DashboardControlModerator();
        var controls = DashboardControls(f, moderator);
        var button = DashboardButton(flow, source.Id);
        var timer = DashboardTimer("tea");
        foreach (
            var denial in new ModeratorAuthorityOutcome[]
            {
                new ModeratorAuthorityOutcome.Revoked(),
                new ModeratorAuthorityOutcome.Unavailable(),
            }
        )
        {
            moderator.Outcome = denial;
            _ = (
                await controls.RunAsync(
                    DashboardSession(f.HostId, AuthRole.Moderator),
                    new(f.HostId),
                    button,
                    CancellationToken.None
                )
            ).ShouldBeOfType<DashboardResult<AutomationManualRunOutcome>.Rejected>();
            _ = (
                await controls.ApplyCountdownAsync(
                    DashboardSession(f.HostId, AuthRole.Moderator),
                    new(f.HostId),
                    timer,
                    CountdownOperation.Start,
                    null,
                    CancellationToken.None
                )
            ).ShouldBeOfType<DashboardResult<AutomationCountdownControlOutcome>.Rejected>();
        }
        _ = (
            await controls.RunAsync(
                DashboardSession(other),
                new(f.HostId),
                button,
                CancellationToken.None
            )
        ).ShouldBeOfType<DashboardResult<AutomationManualRunOutcome>.Rejected>();
        _ = DashboardValue(
                await controls.RunAsync(
                    DashboardSession(other),
                    new(other),
                    button,
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        _ = DashboardValue(
                await controls.RunAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    DashboardButton(flow, new(Guid.NewGuid())),
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        _ = await f.Flows.SetEnabledAsync(new(f.HostId), flow, false, CancellationToken.None);
        _ = DashboardValue(
                await controls.RunAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    button,
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        _ = await f.Flows.DeleteAsync(new(f.HostId), flow, CancellationToken.None);
        _ = DashboardValue(
                await controls.RunAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    button,
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        _ = await f.Features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        _ = DashboardValue(
                await controls.ApplyCountdownAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    timer,
                    CountdownOperation.Start,
                    null,
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationCountdownControlOutcome.Unavailable>();
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DashboardControls_TargetChangedAfterLookup_IsRejectedAtActualAdmission(
        bool replaceSource
    )
    {
        var barrier = new AutomationDispatchTransactionInterleaving();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        var source = Node("manual-run", """{"data":"saved"}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        var flow = await f.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        barrier.Arm();
        var call = DashboardControls(f)
            .RunAsync(
                DashboardSession(f.HostId),
                new(f.HostId),
                DashboardButton(flow, source.Id),
                CancellationToken.None
            );
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (replaceSource)
            {
                var replacement = Node("manual-run", """{"data":"saved"}""");
                _ = (
                    await f.Flows.SaveAsync(
                        Draft(
                            f.HostId,
                            [replacement, action],
                            [Edge(replacement, "flow", action)]
                        ) with
                        {
                            Id = flow,
                        },
                        CancellationToken.None
                    )
                ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            }
            else
            {
                _ = await f.Flows.SetEnabledAsync(
                    new(f.HostId),
                    flow,
                    false,
                    CancellationToken.None
                );
            }
        }
        finally
        {
            barrier.Release();
        }
        DashboardDispatched(await call.WaitAsync(TimeSpan.FromSeconds(5)))
            .Status.ShouldBe(AutomationDispatchStatus.NoMatchingFlow);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task DashboardControls_ReturnedRunStatus_TracksWaitingThenTerminal_ExcludesOtherRunsAndHosts()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"go"}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"done"}""");
        var flow = await f.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var controls = DashboardControls(f);
        var configuration = DashboardButton(flow, source.Id);
        var first = DashboardDispatched(
            await controls.RunAsync(
                DashboardSession(f.HostId),
                new(f.HostId),
                configuration,
                CancellationToken.None
            )
        );
        _ = await controls.RunAsync(
            DashboardSession(f.HostId),
            new(f.HostId),
            configuration,
            CancellationToken.None
        );
        var other = await f.SeedHostAsync("foreign", HostFeatureFlags.Automations);
        var foreignSource = Node("manual-run", """{"data":"foreign"}""");
        var foreignFlow = (
            await f.Flows.SaveAsync(Draft(other, [foreignSource], []), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var foreign = DashboardDispatched(
            await controls.RunAsync(
                DashboardSession(other),
                new(other),
                DashboardButton(foreignFlow, foreignSource.Id),
                CancellationToken.None
            )
        );
        var ids = first.RunIds.AddRange(foreign.RunIds);
        var status = DashboardValue(
                await controls.ReadRunsAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    ids,
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationRunStatusOutcome.Available>();
        status.Runs.ShouldHaveSingleItem().State.ShouldBe(AutomationFlowRunState.Waiting);
        first.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        f.Chat.Messages.ShouldBeEmpty();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        status = DashboardValue(
                await controls.ReadRunsAsync(
                    DashboardSession(f.HostId),
                    new(f.HostId),
                    ids,
                    CancellationToken.None
                )
            )
            .ShouldBeOfType<AutomationRunStatusOutcome.Available>();
        var completed = status.Runs.ShouldHaveSingleItem();
        completed.Id.ShouldBe(first.RunIds.Single());
        completed.State.ShouldBe(AutomationFlowRunState.Completed);
        _ = completed.CompletedAtUtc.ShouldNotBeNull();
        f.Chat.Messages.ShouldBe(["done", "done"]);
    }

    [Test]
    public async Task DashboardControls_Countdown_DefaultOverrideIdempotencyAndReset_UseNativeLifecycleAndRestartFence()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"tea","event":"Started","remaining-seconds":0}""",
            "started"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"tea","event":"Reset","remaining-seconds":0}""",
            "reset"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"tea","event":"Cancelled","remaining-seconds":0}""",
            "cancelled"
        );
        var controls = DashboardControls(f);
        var session = DashboardSession(f.HostId);
        var host = new DashboardHostId(f.HostId);
        var timer = DashboardTimer("tea");
        var started = DashboardCountdown(
            await controls.ApplyCountdownAsync(
                session,
                host,
                timer,
                CountdownOperation.Start,
                null,
                CancellationToken.None
            )
        );
        (started.DeadlineUtc - started.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(10));
        var repeat = DashboardCountdown(
            await controls.ApplyCountdownAsync(
                session,
                host,
                timer,
                CountdownOperation.Start,
                TimeSpan.FromSeconds(90),
                CancellationToken.None
            )
        );
        repeat.ShouldBe(started);
        var reset = DashboardCountdown(
            await controls.ApplyCountdownAsync(
                session,
                host,
                timer,
                CountdownOperation.Reset,
                TimeSpan.FromSeconds(20),
                CancellationToken.None
            )
        );
        reset.OccurrenceId.ShouldNotBe(started.OccurrenceId);
        (reset.DeadlineUtc - reset.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(20));
        var cancelled = DashboardCountdown(
            await controls.ApplyCountdownAsync(
                session,
                host,
                timer,
                CountdownOperation.Cancel,
                null,
                CancellationToken.None
            )
        );
        cancelled.State.ShouldBe(AutomationCountdownState.Cancelled);
        DashboardCountdown(
                await controls.ApplyCountdownAsync(
                    session,
                    host,
                    timer,
                    CountdownOperation.Cancel,
                    null,
                    CancellationToken.None
                )
            )
            .ShouldBe(cancelled);
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["started", "reset", "cancelled"]);
        foreach (
            var invalid in new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1500),
                TimeSpan.FromDays(366),
            }
        )
        {
            _ = DashboardValue(
                    await controls.ApplyCountdownAsync(
                        session,
                        host,
                        timer,
                        CountdownOperation.Reset,
                        invalid,
                        CancellationToken.None
                    )
                )
                .ShouldBeOfType<AutomationCountdownControlOutcome.Unavailable>();
        }
        DashboardCountdown(
                await controls.ReadCountdownAsync(session, host, timer, CancellationToken.None)
            )
            .ShouldBe(cancelled);
        _ = await controls.ApplyCountdownAsync(
            session,
            host,
            timer,
            CountdownOperation.Start,
            null,
            CancellationToken.None
        );
        var restarted = new AutomationCountdownService(f.Database, f.Clock, () => f.Runtime);
        var restartedControl = new AutomationCountdownControlService(
            f.Database,
            restarted,
            f.Catalog
        );
        (await restartedControl.ReadAsync(new(f.HostId), "tea", CancellationToken.None))
            .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
            .Countdown.State.ShouldBe(AutomationCountdownState.Cancelled);
        await restarted.InitializeAsync(CancellationToken.None);
        f.Clock.Advance(TimeSpan.FromSeconds(11));
        await restarted.TickAsync(await HostForExpandedAsync(f), [], CancellationToken.None);
        await f.Runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(4);
    }

    [Test]
    public async Task DashboardControls_CountdownGenerationChange_ProjectsCancelledBeforeObservationTick()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var controls = DashboardControls(f);
        var session = DashboardSession(f.HostId);
        var host = new DashboardHostId(f.HostId);
        var timer = DashboardTimer("generation");
        _ = await controls.ApplyCountdownAsync(
            session,
            host,
            timer,
            CountdownOperation.Start,
            null,
            CancellationToken.None
        );
        _ = await f.Features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        _ = await f.Features.EnableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        DashboardCountdown(
            await controls.ReadCountdownAsync(session, host, timer, CancellationToken.None)
        )
            .State.ShouldBe(AutomationCountdownState.Cancelled);
        _ = await controls.ApplyCountdownAsync(
            session,
            host,
            timer,
            CountdownOperation.Start,
            null,
            CancellationToken.None
        );
        DashboardCountdown(
            await controls.ReadCountdownAsync(session, host, timer, CancellationToken.None)
        )
            .State.ShouldBe(AutomationCountdownState.Running);
        f.Clock.Advance(TimeSpan.FromSeconds(11));
        await f.Countdowns.TickAsync(await HostForExpandedAsync(f), [], CancellationToken.None);
        DashboardCountdown(
            await controls.ReadCountdownAsync(session, host, timer, CancellationToken.None)
        )
            .State.ShouldBe(AutomationCountdownState.Finished);
        var restarted = new AutomationCountdownService(f.Database, f.Clock, () => f.Runtime);
        var restartedControl = new AutomationCountdownControlService(
            f.Database,
            restarted,
            f.Catalog
        );
        (await restartedControl.ReadAsync(new(f.HostId), "generation", CancellationToken.None))
            .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
            .Countdown.State.ShouldBe(AutomationCountdownState.Finished);
    }

    private static DashboardAutomationControls DashboardControls(
        RuntimeFixture f,
        DashboardControlModerator? moderator = null
    ) =>
        new(
            new(f.Database, moderator ?? new DashboardControlModerator()),
            new(f.Database, f.Runtime, f.Catalog, f.Clock),
            f.Queries,
            new(f.Database, f.Countdowns, f.Catalog)
        );

    private static AuthenticatedSession DashboardSession(
        int hostId,
        AuthRole role = AuthRole.Streamer
    )
    {
        var host = new BotHostChoice(hostId, "streamer", "Streamer", role);
        return new()
        {
            IsAuthenticated = true,
            UserId = "operator-id",
            Login = "operator",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static DashboardWidgetConfiguration.Automation DashboardButton(
        AutomationFlowId flow,
        AutomationNodeId source
    ) =>
        new(
            new DashboardBinding.Configured(new DashboardTarget.Automation(flow, source)),
            new DashboardTextOverride.Inherit()
        );

    private static DashboardWidgetConfiguration.Countdown DashboardTimer(string name) =>
        new(
            new DashboardBinding.Configured(new DashboardTarget.Countdown(name)),
            TimeSpan.FromSeconds(10)
        );

    private static T DashboardValue<T>(DashboardResult<T> value) =>
        value.ShouldBeOfType<DashboardResult<T>.Succeeded>().Value;

    private static AutomationDispatchOutcome DashboardDispatched(
        DashboardResult<AutomationManualRunOutcome> value
    ) => DashboardValue(value).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>().Dispatch;

    private static AutomationCountdownSnapshot DashboardCountdown(
        DashboardResult<AutomationCountdownControlOutcome> value
    ) =>
        DashboardValue(value)
            .ShouldBeOfType<AutomationCountdownControlOutcome.Available>()
            .Countdown;

    private sealed class DashboardControlModerator : IModeratorAuthorityService
    {
        internal ModeratorAuthorityOutcome Outcome { get; set; } =
            new ModeratorAuthorityOutcome.Granted();

        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int hostId,
            CancellationToken ct
        ) => Task.FromResult(Outcome);
    }
}
