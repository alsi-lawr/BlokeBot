using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CountdownStart_RealFlowsKeepLegacySuccessAndRouteOneSpecificOutcome(
        bool legacy
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var graph = CountdownGraphFor(f.HostId, legacy);
        var flow = (await f.Flows.SaveAsync(graph.Draft, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        if (legacy)
        {
            graph = await TransferLegacyCountdownAsync(f, flow);
            flow = graph.Draft.Id!.Value;
        }
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"tea","event":"Started","remaining-seconds":0}""",
            "lifecycle"
        );
        var updates = 0;
        using var ui = f.UiChanges.Subscribe(
            new AutomationUiChange.Countdown(new(f.HostId), "tea"),
            ObserverIdentity.Named("start-once"),
            (_, _) =>
            {
                updates++;
                return ValueTask.CompletedTask;
            }
        );
        var manual = new AutomationManualRunService(f.Database, f.Runtime, f.Catalog, f.Clock);
        var first = (
            await manual.RunAsync(new(f.HostId), flow, default)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        await f.Runtime.ResumeDueAsync(default);
        await using var db = await f.Database.CreateDbContextAsync();
        var timer = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
        f.Chat.Messages.Count(value => value == "common").ShouldBe(1);
        f.Chat.Messages.Count(value => value == "started").ShouldBe(legacy ? 0 : 1);
        f.Chat.Messages.ShouldNotContain("already");
        var second = (
            await manual.RunAsync(new(f.HostId), flow, default)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        await f.Runtime.ResumeDueAsync(default);
        var duplicate = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
        SameCountdownOccurrence(timer, duplicate);
        updates.ShouldBe(1);
        f.Chat.Messages.Count(value => value == "lifecycle").ShouldBe(1);
        f.Chat.Messages.Count(value => value == "common").ShouldBe(2);
        f.Chat.Messages.Count(value => value == "started").ShouldBe(legacy ? 0 : 1);
        f.Chat.Messages.Count(value => value == "already").ShouldBe(legacy ? 0 : 1);
        f.Chat.Messages.Count(value => value == "converged").ShouldBe(legacy ? 0 : 2);
        var firstRun = first.Dispatch.RunIds.ShouldHaveSingleItem();
        var secondRun = second.Dispatch.RunIds.ShouldHaveSingleItem();
        foreach (
            var (run, specific) in new[] { (firstRun, "started"), (secondRun, "already-running") }
        )
        {
            var trace = await ReadTraceAsync(f, new(run.Value));
            CountdownBranchPorts(trace, graph.Start.Id).ShouldBe(["complete", specific]);
            trace
                .Events.Count(entry =>
                    entry.Event.Node?.Id == graph.Start.Id
                    && entry.Event.Kind == AutomationTraceEventKind.Result
                )
                .ShouldBe(1);
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(
                    (
                        await db.AutomationFlowRuns.SingleAsync(value => value.Id == run.Value)
                    ).DefinitionJson
                )
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>()
                .Flow;
            frozen
                .Nodes.Single(node => node.Id == graph.Start.Id.Value)
                .DefinitionSchemaVersion.ShouldBe(1);
            if (legacy)
            {
                frozen
                    .Edges.Where(edge => edge.SourceNodeId == graph.Start.Id.Value)
                    .Select(edge => edge.SourcePortId)
                    .ShouldBe(["complete"]);
            }
        }
    }

    [Test]
    public async Task CountdownStart_OvertakingBeforeTransactionPreservesWinnerAndOneLifecycle()
    {
        var barrier = new AutomationDispatchTransactionInterleaving();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"race","event":"Started","remaining-seconds":0}""",
            "lifecycle"
        );
        await f.Countdowns.InitializeAsync(default);
        barrier.Arm();
        var overtaken = f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("race", TimeSpan.FromSeconds(999), CountdownOperation.Start),
            default
        );
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            _ = (
                await f.Countdowns.ApplyAsync(
                    new(f.HostId),
                    new("race", TimeSpan.FromSeconds(20), CountdownOperation.Start),
                    default
                )
            ).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        }
        finally
        {
            barrier.Release();
        }
        _ = (await overtaken).ShouldBeOfType<AutomationCountdownApplyOutcome.AlreadyRunning>();
        await f.Runtime.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["lifecycle"]);
        await using var db = await f.Database.CreateDbContextAsync();
        var timer = await db.AutomationCountdowns.SingleAsync();
        timer.DeadlineUtc.ShouldBe(f.Clock.GetUtcNow().AddSeconds(20).UtcDateTime);
        timer.ProcessId.ShouldBe(f.Countdowns.ProcessId);
    }

    [Test]
    public async Task CountdownStart_DuplicateDuringPostCommitUiAwaitDoesNotWriteOrEmitAgain()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"race","event":"Started","remaining-seconds":0}""",
            "lifecycle"
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = 0;
        using var subscription = f.UiChanges.Subscribe(
            new AutomationUiChange.Countdown(new(f.HostId), "race"),
            ObserverIdentity.Named("after-commit"),
            async (_, _) =>
            {
                updates++;
                _ = entered.TrySetResult();
                await release.Task;
            }
        );
        var start = f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("race", TimeSpan.FromSeconds(20), CountdownOperation.Start),
            default
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var db = await f.Database.CreateDbContextAsync();
            var winner = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            _ = (
                await f.Countdowns.ApplyAsync(
                    new(f.HostId),
                    new("race", TimeSpan.FromSeconds(999), CountdownOperation.Start),
                    default
                )
            ).ShouldBeOfType<AutomationCountdownApplyOutcome.AlreadyRunning>();
            SameCountdownOccurrence(
                winner,
                await db.AutomationCountdowns.AsNoTracking().SingleAsync()
            );
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            updates.ShouldBe(1);
        }
        finally
        {
            _ = release.TrySetResult();
        }
        _ = (await start).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        await f.Runtime.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["lifecycle"]);
    }

    [Test]
    [Arguments(AutomationNodeFailurePolicy.Stop)]
    [Arguments(AutomationNodeFailurePolicy.Continue)]
    public async Task CountdownStart_EmissionFailureNeverClaimsSpecificSuccessAndRetryObservesCommittedTimer(
        AutomationNodeFailurePolicy policy
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var graph = CountdownGraphFor(f.HostId, false, policy);
        var flow = (await f.Flows.SaveAsync(graph.Draft, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var countdowns = new AutomationCountdownService(
            f.Database,
            f.Clock,
            () => throw new IOException("Injected lifecycle emission failure"),
            f.UiChanges
        );
        var actions = new AutomationActionExecutor(
            f.Features,
            f.Chat,
            new NoOverlayCues(),
            f.Expressions,
            countdowns
        );
        var runtime = new AutomationRuntimeService(
            f.Database,
            f.Catalog,
            f.Flows,
            actions,
            f.Clock,
            f.UiChanges,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationRuntimeService>.Instance
        );
        var manual = new AutomationManualRunService(f.Database, runtime, f.Catalog, f.Clock);
        var failed = (await manual.RunAsync(new(f.HostId), flow, default))
            .ShouldBeOfType<AutomationManualRunOutcome.Dispatched>()
            .Dispatch.RunIds.ShouldHaveSingleItem();
        var trace = await ReadTraceAsync(f, new(failed.Value));
        CountdownBranchPorts(trace, graph.Start.Id).ShouldBeEmpty();
        var result = trace.Events.Single(entry =>
            entry.Event.Node?.Id == graph.Start.Id
            && entry.Event.Kind == AutomationTraceEventKind.Result
        );
        result.Event.Outcome.ShouldBe(
            policy == AutomationNodeFailurePolicy.Continue
                ? AutomationTraceOutcome.ContinuedAfterFailure
                : AutomationTraceOutcome.Failed
        );
        f.Chat.Messages.Order()
            .ShouldBe(
                policy == AutomationNodeFailurePolicy.Continue ? ["common", "converged"] : []
            );
        await using var db = await f.Database.CreateDbContextAsync();
        var committed = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
        var retried = (await manual.RunAsync(new(f.HostId), flow, default))
            .ShouldBeOfType<AutomationManualRunOutcome.Dispatched>()
            .Dispatch.RunIds.ShouldHaveSingleItem();
        CountdownBranchPorts(await ReadTraceAsync(f, new(retried.Value)), graph.Start.Id)
            .ShouldBe(["complete", "already-running"]);
        SameCountdownOccurrence(
            committed,
            await db.AutomationCountdowns.AsNoTracking().SingleAsync()
        );
        f.Chat.Messages.ShouldNotContain("started");
        f.Chat.Messages.Count(value => value == "already").ShouldBe(1);
    }

    [Test]
    [Arguments(AutomationScenarioEffectResult.Succeeded, false, AutomationNodeFailurePolicy.Stop)]
    [Arguments(AutomationScenarioEffectResult.Succeeded, true, AutomationNodeFailurePolicy.Stop)]
    [Arguments(AutomationScenarioEffectResult.Started, true, AutomationNodeFailurePolicy.Stop)]
    [Arguments(
        AutomationScenarioEffectResult.AlreadyRunning,
        true,
        AutomationNodeFailurePolicy.Stop
    )]
    [Arguments(AutomationScenarioEffectResult.Failed, true, AutomationNodeFailurePolicy.Stop)]
    [Arguments(AutomationScenarioEffectResult.Failed, true, AutomationNodeFailurePolicy.Continue)]
    public async Task CountdownStart_SavedScenarioUsesDeclaredResultAndSharedRoutingWithoutLiveEffects(
        AutomationScenarioEffectResult effect,
        bool declared,
        AutomationNodeFailurePolicy policy
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var graph = CountdownGraphFor(f.HostId, false, policy);
        var flow = (await f.Flows.SaveAsync(graph.Draft, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var fixture = f.Scenarios.CreateDefaultFixture(graph.Draft, graph.Source.Id, 1) with
        {
            Effects = declared ? [new(graph.Start.Id, effect)] : [],
        };
        var saved = await f.Scenarios.SaveAsync(
            new(f.HostId),
            flow,
            null,
            "Countdown",
            fixture,
            default
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var reloaded = (
            await f.Scenarios.ListAsync(new(f.HostId), flow, default)
        ).ShouldHaveSingleItem();
        reloaded.Fixture.Effects.ShouldBe(fixture.Effects);
        if (
            declared
            && effect
                is AutomationScenarioEffectResult.Succeeded
                    or AutomationScenarioEffectResult.Failed
        )
        {
            using var json = JsonDocument.Parse(
                AutomationScenarioSerialization.Serialize(reloaded.Fixture)
            );
            json.RootElement.GetProperty("effects")[0]
                .GetProperty("result")
                .GetInt32()
                .ShouldBe(effect == AutomationScenarioEffectResult.Succeeded ? 0 : 1);
        }
        writes.Armed = true;
        var result = await f.Scenarios.RunSavedAsync(new(f.HostId), flow, saved.Id!.Value, default);
        var stops =
            effect == AutomationScenarioEffectResult.Failed
            && policy == AutomationNodeFailurePolicy.Stop;
        var traceId = stops
            ? result.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>().TraceId
            : result.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>().TraceId;
        var nodes = stops
            ? result.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>().Nodes
            : result.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>().Nodes;
        var trace = await ReadTraceAsync(f, traceId);
        CountdownBranchPorts(trace, graph.Start.Id)
            .ShouldBe(
                effect == AutomationScenarioEffectResult.Failed
                    ? []
                    :
                    [
                        "complete",
                        effect == AutomationScenarioEffectResult.AlreadyRunning
                            ? "already-running"
                            : "started",
                    ]
            );
        string[] expectedMessages =
            effect == AutomationScenarioEffectResult.Failed
                ? policy == AutomationNodeFailurePolicy.Continue
                    ? ["common", "converged"]
                    : []
                :
                [
                    "common",
                    "converged",
                    effect == AutomationScenarioEffectResult.AlreadyRunning ? "already" : "started",
                ];
        var selected = graph
            .Draft.Nodes.Where(node =>
                node.Definition.TypeId == "send-chat" && nodes.Any(value => value.NodeId == node.Id)
            )
            .Select(node => node.Definition.Configuration.GetProperty("message").GetString()!)
            .Order();
        selected.ShouldBe(expectedMessages.Order());
        nodes.Count(node => node.NodeId == graph.Start.Id).ShouldBe(1);
        writes.Writes.ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationEventReceipts.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments(AutomationScenarioEffectResult.Started)]
    [Arguments(AutomationScenarioEffectResult.AlreadyRunning)]
    public async Task CountdownStart_SpecificFixturesAreRejectedForAnotherActionWithoutTraceWrites(
        AutomationScenarioEffectResult effect
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"go"}""");
        var send = Node("send-chat", """{"message":"hello"}""");
        var draft = Draft(f.HostId, [source, send], [Edge(source, "flow", send)]);
        var fixture = f.Scenarios.CreateDefaultFixture(draft, source.Id, 1) with
        {
            Effects = [new(send.Id, effect)],
        };
        (await f.Scenarios.RunAsync(draft, fixture, default))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>()
            .Errors.ShouldContain(error =>
                error.NodeId == send.Id && error.Code == "scenario-effect-invalid"
            );
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationTraces.CountAsync()).ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CountdownStart_ExistingScenarioSelectorNormalizesLegacyDefaultAndEditsSpecificFixture(
        bool legacy
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var graph = CountdownGraphFor(f.HostId, false);
        var fixture = f.Scenarios.CreateDefaultFixture(graph.Draft, graph.Source.Id, 1) with
        {
            Effects = legacy ? [new(graph.Start.Id, AutomationScenarioEffectResult.Succeeded)] : [],
        };
        var editor = new AutomationScenarioEditor(fixture);
        var nodes = graph
            .Draft.Nodes.Select(node =>
                AutomationEditorNode.Restore(
                    node,
                    f.Catalog.ValidatePersistedDefinition(node.Definition, node.InputBindings)
                        .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
                        .Definition
                )
            )
            .ToArray();
        using var ui = new BunitContext();
        var panel = ui.Render<AutomationScenarioPanel>(parameters =>
            parameters.Add(value => value.Editor, editor).Add(value => value.Nodes, nodes)
        );
        var selector = panel.Find($"[data-scenario-effect-node='{graph.Start.Id.Value}']");
        selector.GetAttribute("value").ShouldBe("Started");
        editor.Fixture.Effects.ShouldBe(fixture.Effects);
        selector.Change("Started");
        editor
            .Fixture.Effects.Single(effect => effect.NodeId == graph.Start.Id)
            .Result.ShouldBe(AutomationScenarioEffectResult.Started);
        selector.Change("AlreadyRunning");
        editor
            .Fixture.Effects.Single(effect => effect.NodeId == graph.Start.Id)
            .Result.ShouldBe(AutomationScenarioEffectResult.AlreadyRunning);
        selector.Change("Failed");
        editor
            .Fixture.Effects.Single(effect => effect.NodeId == graph.Start.Id)
            .Result.ShouldBe(AutomationScenarioEffectResult.Failed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CountdownStart_InvalidAndUnavailableRemainDistinctFromRunningSuccess(
        bool missing
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var control = new AutomationCountdownControlService(f.Database, f.Countdowns, f.Catalog);
        _ = (
            await control.ApplyAsync(
                new(f.HostId),
                new("tea", TimeSpan.Zero, CountdownOperation.Start),
                default
            )
        ).ShouldBeOfType<AutomationCountdownControlOutcome.Unavailable>();
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        _ = (
            await f.Countdowns.ApplyAsync(
                new(f.HostId),
                new("tea", TimeSpan.FromSeconds(20), CountdownOperation.Start),
                default
            )
        ).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        var existing = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
        var host = missing ? new AutomationHostId(int.MaxValue) : new AutomationHostId(f.HostId);
        if (!missing)
        {
            _ = await db
                .Hosts.Where(value => value.Id == f.HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(value => value.EnabledFeatures, HostFeatureFlags.None)
                );
        }
        _ = (
            await f.Countdowns.ApplyAsync(
                host,
                new("tea", TimeSpan.FromSeconds(999), CountdownOperation.Start),
                default
            )
        ).ShouldBeOfType<AutomationCountdownApplyOutcome.Unavailable>();
        var failed = (
            await f.Actions.ExecuteAsync(
                host,
                new CountdownActionConfiguration(
                    "tea",
                    TimeSpan.FromSeconds(999),
                    CountdownOperation.Start
                ),
                ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationExpressionSource
                >.Empty,
                ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue>.Empty,
                Context(f.HostId),
                default
            )
        ).ShouldBeOfType<AutomationActionOutcome.Failed>();
        failed.Code.ShouldBe("countdown-unavailable");
        _ = AutomationNodeEvaluation
            .Action(failed, f.Clock.GetUtcNow().UtcDateTime, "action-succeeded")
            .ShouldBeOfType<AutomationNodeExecution.Failed>();
        SameCountdownOccurrence(
            existing,
            await db.AutomationCountdowns.AsNoTracking().SingleAsync()
        );
    }

    [Test]
    [Arguments("reset-countdown")]
    [Arguments("cancel-countdown")]
    public async Task CountdownStart_ScenarioSupportDoesNotExecuteResetOrCancel(string action)
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"go"}""");
        var unsupported = Node(
            action,
            action == "cancel-countdown"
                ? """{"name":"tea"}"""
                : """{"name":"tea","duration-seconds":20}"""
        );
        var graph = Draft(f.HostId, [source, unsupported], [Edge(source, "flow", unsupported)]);
        var fixture = f.Scenarios.CreateDefaultFixture(graph, source.Id, 1);
        (await f.Scenarios.RunAsync(graph, fixture, default))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>()
            .Errors.ShouldContain(error =>
                error.Code == "scenario-simulation-unsupported" && error.NodeId == unsupported.Id
            );
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
    }

    private static CountdownGraph CountdownGraphFor(
        int host,
        bool legacy,
        AutomationNodeFailurePolicy policy = AutomationNodeFailurePolicy.Stop
    )
    {
        var source = Node("manual-run", """{"data":"go"}""");
        var start = Node("start-countdown", """{"name":"tea","duration-seconds":20}""", policy);
        var common = Node("send-chat", """{"message":"common"}""");
        if (legacy)
        {
            return new(
                Draft(
                    host,
                    [source, start, common],
                    [Edge(source, "flow", start), Edge(start, "complete", common)]
                ),
                source,
                start
            );
        }
        var first = Node("send-chat", """{"message":"started"}""");
        var duplicate = Node("send-chat", """{"message":"already"}""");
        var converged = Node("send-chat", """{"message":"converged"}""");
        return new(
            Draft(
                host,
                [source, start, common, first, duplicate, converged],
                [
                    Edge(source, "flow", start),
                    Edge(start, "complete", common),
                    Edge(start, "started", first),
                    Edge(start, "already-running", duplicate),
                    Edge(start, "complete", converged),
                    Edge(start, "started", converged),
                    Edge(start, "already-running", converged),
                ]
            ),
            source,
            start
        );
    }

    private static ImmutableArray<string?> CountdownBranchPorts(
        AutomationTraceSnapshot trace,
        AutomationNodeId node
    ) =>
        [
            .. trace
                .Events.Where(entry =>
                    entry.Event.Node?.Id == node
                    && entry.Event.Kind == AutomationTraceEventKind.Branch
                )
                .Select(entry => entry.Event.SelectedPort?.Value),
        ];

    private static void SameCountdownOccurrence(
        AutomationCountdown expected,
        AutomationCountdown actual
    )
    {
        (
            actual.OccurrenceId,
            actual.ProcessId,
            actual.AutomationGeneration,
            actual.StartedAtUtc,
            actual.ObservedAtUtc,
            actual.DeadlineUtc
        ).ShouldBe(
            (
                expected.OccurrenceId,
                expected.ProcessId,
                expected.AutomationGeneration,
                expected.StartedAtUtc,
                expected.ObservedAtUtc,
                expected.DeadlineUtc
            )
        );
        actual.IsRunning.ShouldBeTrue();
        actual.WasCancelled.ShouldBeFalse();
    }

    private static async Task<CountdownGraph> TransferLegacyCountdownAsync(
        RuntimeFixture f,
        AutomationFlowId flow
    )
    {
        var exported = (
            await new ConfigurationDocumentExporter(
                f.Database,
                new(),
                f.Catalog,
                f.Flows,
                f.Clock,
                f.Scenarios
            ).ExportAsync(
                f.HostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                    new(false, false, false)
                )
                {
                    AutomationFlowIds = new HashSet<Guid> { flow.Value },
                },
                default
            )
        )
            .ShouldBeOfType<ConfigurationExportOutcome.Success>()
            .Document;
        var selection = new ConfigurationImportSelection(
            f.HostId,
            [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags>()
        );
        await using var db = await f.Database.CreateDbContextAsync();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var references = await ConfigurationImportReferencePlan.BuildAsync(
                db,
                f.HostId,
                exported,
                selection,
                default
            );
            var host = await db.Hosts.SingleAsync(host => host.Id == f.HostId);
            var staged = await new AutomationConfigurationTransferAdapter(
                f.Flows,
                f.Catalog,
                f.Clock,
                f.Scenarios
            ).StageAsync(
                db,
                host,
                exported.Sections.Automations!,
                selection.Sections[0],
                references,
                default
            );
            staged.Issues.ShouldBeEmpty();
            await transaction.CommitAsync();
        }
        db.ChangeTracker.Clear();
        var row = await db
            .AutomationFlows.Include(flow => flow.Nodes)
            .Include(flow => flow.Edges)
            .SingleAsync();
        var restored = AutomationFlowService
            .RestoreDraft(row)
            .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>()
            .Draft;
        var start = restored.Nodes.Single(node => node.Definition.TypeId == "start-countdown");
        start.Definition.SchemaVersion.ShouldBe(1);
        start.Definition.Configuration.GetProperty("name").GetString().ShouldBe("tea");
        start.Definition.Configuration.GetProperty("duration-seconds").GetInt32().ShouldBe(20);
        restored
            .Edges.Where(edge => edge.SourceNodeId == start.Id)
            .Select(edge => edge.SourcePortId)
            .ShouldBe([new AutomationPortId("complete")]);
        _ = (
            await f.Flows.SaveAsync(restored, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        return new(
            restored,
            restored.Nodes.Single(node => node.Definition.TypeId == "manual-run"),
            start
        );
    }

    private sealed record CountdownGraph(
        AutomationFlowDraft Draft,
        AutomationFlowDraftNode Source,
        AutomationFlowDraftNode Start
    );
}
