using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.Dashboards;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task ViewerCountdown_TypedCommandCapturesMinimalLifecycleAfterInitiatingRunCompletes()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var start = Node(
            "start-countdown",
            """{"owner":"Viewer","name":"","duration-seconds":0}""",
            bindings: Bindings("name", AutomationInputBindingMode.Connected)
                .Add(new("duration-seconds"), new(AutomationInputBindingMode.Connected, null))
        );
        _ = await f.SaveAsync(
            [source, start],
            [
                Edge(source, "flow", start),
                Edge(source, "argument.name", start, "name", AutomationEdgeKind.Data),
                Edge(
                    source,
                    "argument.seconds",
                    start,
                    "duration-seconds",
                    AutomationEdgeKind.Data
                ),
            ]
        );
        var finish = Node(
            "countdown-lifecycle",
            """{"timers":"AnyViewer","name":"","event":"Finished","remaining-seconds":0}"""
        );
        var message = Node(
            "send-chat",
            """{"message":""}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var name = Node(
            "send-chat",
            """{"message":""}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await f.SaveAsync(
            [finish, message, name],
            [
                Edge(finish, "flow", message),
                Edge(message, "complete", name),
                Edge(
                    finish,
                    "initiating-display-name",
                    message,
                    "message",
                    AutomationEdgeKind.Data
                ),
                Edge(finish, "timer-name", name, "message", AutomationEdgeKind.Data),
            ]
        );
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"name":"tea sentinel","event":"Finished","remaining-seconds":0}""",
            "wrong fixed source"
        );
        var context = TypedCommandContext(f.HostId, "2 tea sentinel") with
        {
            Actor = new("stable-viewer-sentinel", "private-login-sentinel", "Shared display"),
        };
        var run = (
            await f.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        (await f.Runtime.ResumeAsync(run, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        await using var db = await f.Database.CreateDbContextAsync();
        var timer = await db.AutomationCountdowns.AsNoTracking().SingleAsync();
        timer.Name.ShouldBe("tea sentinel");
        timer.OwnerViewerId.ShouldBe("stable-viewer-sentinel");
        timer.OwnerDisplayName.ShouldBe("Shared display");
        (timer.DeadlineUtc - timer.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(2));
        JsonSerializer.Serialize(timer).ShouldNotContain("private-login-sentinel");
        JsonSerializer.Serialize(timer).ShouldNotContain("2 tea sentinel");
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        await f.Countdowns.TickAsync(await HostForExpandedAsync(f), [], default);
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        await f.Runtime.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBe(["Shared display", "tea sentinel"]);
        var lifecycle = await db
            .AutomationFlowRuns.AsNoTracking()
            .SingleAsync(value => value.Id != run.Value);
        var captured = AutomationRuntimeSerialization
            .RestoreContext(lifecycle.ContextSchemaVersion, lifecycle.ContextJson)
            .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
            .Context;
        captured.Actor.ShouldBeNull();
        captured.Arguments.ShouldBeEmpty();
        captured.CommandArguments.ShouldBeNull();
        lifecycle.ContextJson.ShouldNotContain("private-login-sentinel");
        var trace = JsonSerializer.Serialize(await ReadTraceAsync(f, new(lifecycle.Id)));
        trace.ShouldNotContain("stable-viewer-sentinel");
        trace.ShouldNotContain("Shared display");
        trace.ShouldNotContain("tea sentinel");
    }

    [Test]
    public async Task ViewerCountdown_IdentityDuplicateOwnerAndCurrentManagerControlsAreOccurrenceScoped()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var action = new CountdownActionConfiguration(
            "same",
            TimeSpan.FromSeconds(10),
            CountdownOperation.Start,
            CountdownTimerOwner.Viewer
        );
        async Task<AutomationCountdownApplyOutcome> Apply(
            string id,
            CountdownActionConfiguration value,
            string display = "Same display"
        ) =>
            await f.Countdowns.ApplyPreparedAsync(
                new(f.HostId),
                AutomationNativeActionPreparation
                    .Countdown(
                        value,
                        ImmutableDictionary<
                            AutomationConfigurationFieldId,
                            AutomationResolvedValue
                        >.Empty,
                        new(id, "not retained", display),
                        f.Clock.GetUtcNow()
                    )
                    .ShouldBeOfType<AutomationNativePreparation<AutomationPreparedCountdown>.Prepared>()
                    .Payload,
                default
            );
        _ = (await Apply("one", action)).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        _ = (await Apply("two", action)).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        await using var db = await f.Database.CreateDbContextAsync();
        var original = await db
            .AutomationCountdowns.AsNoTracking()
            .SingleAsync(value => value.OwnerViewerId == "one");
        _ = (
            await Apply(
                "one",
                action with
                {
                    Duration = TimeSpan.FromSeconds(99),
                },
                "Changed display"
            )
        ).ShouldBeOfType<AutomationCountdownApplyOutcome.AlreadyRunning>();
        var unchanged = await db
            .AutomationCountdowns.AsNoTracking()
            .SingleAsync(value => value.OwnerViewerId == "one");
        SameCountdownOccurrence(original, unchanged);
        unchanged.OwnerDisplayName.ShouldBe("Same display");
        _ = (
            await Apply("other", action with { Operation = CountdownOperation.Reset })
        ).ShouldBeOfType<AutomationCountdownApplyOutcome.Unavailable>();
        var controls = DashboardControls(f);
        var session = DashboardSession(f.HostId);
        var target = (await controls.ReadLiveCountdownsAsync(session, new(f.HostId), default))
            .ShouldBeOfType<DashboardResult<
                ImmutableArray<AutomationCountdownSnapshot>
            >.Succeeded>()
            .Value.Single(value => value.OwnerViewerId == "one");
        var widget = DashboardTimer("saved fixed target");
        var reset = DashboardCountdown(
            await controls.ApplyCountdownAsync(
                session,
                new(f.HostId),
                widget,
                CountdownOperation.Reset,
                TimeSpan.FromSeconds(30),
                default,
                target
            )
        );
        reset.OwnerViewerId.ShouldBe("one");
        reset.OwnerDisplayName.ShouldBe("Same display");
        (reset.DeadlineUtc - reset.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(30));
        _ = DashboardValue(
                await controls.ApplyCountdownAsync(
                    session,
                    new(f.HostId),
                    widget,
                    CountdownOperation.Cancel,
                    null,
                    default,
                    target
                )
            )
            .ShouldBeOfType<AutomationCountdownControlOutcome.Unavailable>();
        _ = (
            await controls.ApplyCountdownAsync(
                session,
                new(f.HostId + 1),
                widget,
                CountdownOperation.Cancel,
                null,
                default,
                reset
            )
        ).ShouldBeOfType<DashboardResult<AutomationCountdownControlOutcome>.Rejected>();
        _ = DashboardCountdown(
            await controls.ApplyCountdownAsync(
                session,
                new(f.HostId),
                widget,
                CountdownOperation.Cancel,
                null,
                default,
                reset
            )
        );
        (
            await db
                .AutomationCountdowns.AsNoTracking()
                .Select(value => value.OwnerViewerId)
                .ToArrayAsync()
        ).ShouldBe(["two"]);
        JsonSerializer.Serialize(widget).ShouldNotContain("one");
    }

    [Test]
    [Arguments(CountdownOperation.Reset)]
    [Arguments(CountdownOperation.Cancel)]
    public async Task ViewerCountdown_SupersededFinishCannotPublishOrDeleteReplacement(
        CountdownOperation operation
    )
    {
        var barrier = new AutomationDispatchTransactionInterleaving();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [barrier]);
        _ = await SaveExpandedChatAsync(
            f,
            "countdown-lifecycle",
            """{"timers":"AnyViewer","event":"Finished","remaining-seconds":0}""",
            "old finish"
        );
        var action = new CountdownActionConfiguration(
            "race",
            TimeSpan.FromSeconds(1),
            CountdownOperation.Start,
            CountdownTimerOwner.Viewer
        );
        var prepared = new AutomationPreparedCountdown(action, "owner", "Owner");
        _ = await f.Countdowns.ApplyPreparedAsync(new(f.HostId), prepared, default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        barrier.Arm(operation == CountdownOperation.Cancel ? 0 : 1);
        var tick = f.Countdowns.TickAsync(await HostForExpandedAsync(f), [], default);
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            _ = await f.Countdowns.ApplyPreparedAsync(
                new(f.HostId),
                prepared with
                {
                    Action = action with
                    {
                        Operation = operation,
                        Duration = TimeSpan.FromSeconds(20),
                    },
                },
                default
            );
        }
        finally
        {
            barrier.Release();
        }
        await tick.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Runtime.ResumeDueAsync(default);
        f.Chat.Messages.ShouldBeEmpty();
        await using var db = await f.Database.CreateDbContextAsync();
        var remaining = await db.AutomationCountdowns.AsNoTracking().ToArrayAsync();
        if (operation == CountdownOperation.Reset)
        {
            var timer = remaining.ShouldHaveSingleItem();
            timer.IsRunning.ShouldBeTrue();
            timer.OwnerViewerId.ShouldBe("owner");
            (timer.DeadlineUtc - timer.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(20));
        }
        else
        {
            remaining.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task ViewerCountdown_ScenarioAndLivePreparationRejectFractionsAndMissingIdentityWithoutEffects()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"go"}""");
        var start = Node(
            "start-countdown",
            """{"owner":"Viewer","name":"tea","duration-seconds":1}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("duration-seconds"),
                new(AutomationExpressionLanguage.CurrentVersion, "1.5")
            )
        );
        var graph = Draft(f.HostId, [source, start], [Edge(source, "flow", start)]);
        var fixture = f.Scenarios.CreateDefaultFixture(graph, source.Id, 1);
        _ = (
            await f.Scenarios.RunAsync(graph, fixture, default)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Failed>();
        var flow = (
            await f.Flows.SaveAsync(graph, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var manual = new AutomationManualRunService(f.Database, f.Runtime, f.Catalog, f.Clock);
        _ = (
            await manual.RunAsync(new(f.HostId), flow.FlowId, default)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
        var valid = new CountdownActionConfiguration(
            "tea",
            TimeSpan.FromSeconds(1),
            CountdownOperation.Start,
            CountdownTimerOwner.Viewer
        );
        foreach (
            var (actor, now) in new (AutomationActor?, DateTimeOffset)[]
            {
                (null, f.Clock.GetUtcNow()),
                (new("", "name", "Display"), f.Clock.GetUtcNow()),
                (new("owner", "name", "Display"), DateTimeOffset.MaxValue),
            }
        )
        {
            _ = AutomationNativeActionPreparation
                .Countdown(
                    valid,
                    ImmutableDictionary<
                        AutomationConfigurationFieldId,
                        AutomationResolvedValue
                    >.Empty,
                    actor,
                    now
                )
                .ShouldBeOfType<AutomationNativePreparation<AutomationPreparedCountdown>.Failed>();
        }
    }

    [Test]
    public async Task ViewerCountdown_QuietRestartRemovesOwnerKeyButPreservesFixedTerminalSnapshot()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("fixed", TimeSpan.FromSeconds(10), CountdownOperation.Start),
            default
        );
        _ = await f.Countdowns.ApplyPreparedAsync(
            new(f.HostId),
            new(
                new(
                    "owned",
                    TimeSpan.FromSeconds(10),
                    CountdownOperation.Start,
                    CountdownTimerOwner.Viewer
                ),
                "private-owner",
                "Display"
            ),
            default
        );
        var restarted = new AutomationCountdownService(
            f.Database,
            f.Clock,
            () => f.Runtime,
            TestAutomationUiChanges.Create()
        );
        await restarted.InitializeAsync(default);
        await using var db = await f.Database.CreateDbContextAsync();
        var fixedTimer = (
            await db.AutomationCountdowns.AsNoTracking().ToArrayAsync()
        ).ShouldHaveSingleItem();
        fixedTimer.Name.ShouldBe("fixed");
        fixedTimer.OwnerViewerId.ShouldBeEmpty();
        fixedTimer.WasCancelled.ShouldBeTrue();
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task ViewerCountdown_AuthoredBindingsRoundTripWithoutExportingLiveOwnerAndLegacyEditorStaysEditable()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var source = Node("manual-run", """{"data":"go"}""");
        var action = Node(
            "start-countdown",
            """{"owner":"Viewer","name":"tea","duration-seconds":1}""",
            fields: ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource>
                .Empty.Add(new("name"), new(AutomationExpressionLanguage.CurrentVersion, "'tea'"))
                .Add(new("duration-seconds"), new(AutomationExpressionLanguage.CurrentVersion, "2"))
        );
        var flow = await f.SaveAsync([source, action], [Edge(source, "flow", action)]);
        _ = await f.Countdowns.ApplyPreparedAsync(
            new(f.HostId),
            new(
                new(
                    "runtime-only",
                    TimeSpan.FromSeconds(10),
                    CountdownOperation.Start,
                    CountdownTimerOwner.Viewer
                ),
                "never-export-owner",
                "never-export-display"
            ),
            default
        );
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
        var json = JsonSerializer.Serialize(exported);
        json.ShouldNotContain("never-export-owner");
        json.ShouldNotContain("never-export-display");
        var selection = new ConfigurationImportSelection(
            f.HostId,
            [new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, [])],
            new HashSet<BlokeBot.Persistence.Models.HostFeatureFlags>()
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
            var adapter = new AutomationConfigurationTransferAdapter(
                f.Flows,
                f.Catalog,
                f.Clock,
                f.Scenarios
            );
            var host = await db.Hosts.SingleAsync();
            var preview = await adapter.PreviewAsync(
                db,
                host,
                exported.Sections.Automations!,
                selection.Sections[0],
                references,
                default
            );
            var reviewed = selection.Sections[0] with
            {
                AutomationReview = preview.AutomationReview,
                AutomationEffectsApproved = true,
            };
            var staged = await adapter.StageAsync(
                db,
                host,
                exported.Sections.Automations!,
                reviewed,
                references,
                default
            );
            staged.Issues.ShouldBeEmpty();
            await transaction.CommitAsync();
        }
        db.ChangeTracker.Clear();
        var restored = AutomationFlowService
            .RestoreDraft(
                await db
                    .AutomationFlows.Include(value => value.Nodes)
                    .Include(value => value.Edges)
                    .SingleAsync()
            )
            .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>()
            .Draft;
        var restoredAction = restored.Nodes.Single(value =>
            value.Definition.TypeId == "start-countdown"
        );
        restoredAction.InputBindings.ShouldBe(action.InputBindings);
        restoredAction.Definition.Configuration.GetProperty("owner").GetString().ShouldBe("Viewer");
        foreach (
            var node in new[]
            {
                restoredAction,
                Node("start-countdown", """{"name":"legacy","duration-seconds":3}"""),
            }
        )
        {
            var editor = AutomationEditorNode.Restore(
                node,
                f.Catalog.ValidatePersistedDefinition(node.Definition, node.InputBindings)
                    .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
                    .Definition
            );
            editor.ProjectionPreservesOriginal.ShouldBeTrue();
            JsonElement
                .DeepEquals(editor.Draft().Definition.Configuration, node.Definition.Configuration)
                .ShouldBeTrue();
            editor.Draft().InputBindings.ShouldBe(node.InputBindings);
        }
        var dispatch = await f.Runtime.DispatchAsync(
            new(
                Context(f.HostId, sourceDefinitionId: AutomationDefinitionIds.ManualSource),
                new ManualSourceConfiguration("go")
            ),
            default
        );
        _ = dispatch.RunIds.ShouldHaveSingleItem();
        var executed = await db
            .AutomationCountdowns.AsNoTracking()
            .SingleAsync(value => value.Name == "tea");
        executed.OwnerViewerId.ShouldBe("viewer-id");
        (executed.DeadlineUtc - executed.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(2));
        var fixedSource = Node(
            "countdown-lifecycle",
            """{"name":"legacy","event":"Finished","remaining-seconds":0}"""
        );
        var fixedDraft = Draft(f.HostId, [fixedSource], []);
        var portable = f.Scenarios.CreatePortableFixture(fixedDraft, fixedSource.Id);
        f.Scenarios.IsPortableFixture(fixedDraft, portable).ShouldBeTrue();
        portable
            .Context.Variables.ForExecution()
            .Keys.ShouldNotContain(new AutomationVariableName("initiating-viewer-id"));
    }

    [Test]
    public async Task ViewerCountdown_FeatureDisableClearsOwnedIdentityAndRetainsFixedSnapshotWithoutEvents()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await f.Countdowns.ApplyAsync(
            new(f.HostId),
            new("fixed", TimeSpan.FromSeconds(10), CountdownOperation.Start),
            default
        );
        _ = await f.Countdowns.ApplyPreparedAsync(
            new(f.HostId),
            new(
                new(
                    "owned",
                    TimeSpan.FromSeconds(10),
                    CountdownOperation.Start,
                    CountdownTimerOwner.Viewer
                ),
                "private-owner",
                "Display"
            ),
            default
        );
        _ = await f.Features.DisableAsync(
            f.HostId,
            BlokeBot.Persistence.Models.HostFeatureFlags.Automations,
            default
        );
        await using var db = await f.Database.CreateDbContextAsync();
        var retained = (
            await db.AutomationCountdowns.AsNoTracking().ToArrayAsync()
        ).ShouldHaveSingleItem();
        retained.Name.ShouldBe("fixed");
        retained.OwnerViewerId.ShouldBeEmpty();
        retained.WasCancelled.ShouldBeTrue();
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test, Explicit]
    public async Task ViewerCountdown_PostgreSqlHeldHostLockKeepsDuplicateAndSeparateViewerIdentities()
    {
        var barrier = new CountdownHostLockBarrier();
        await using var f = await RuntimeFixture.CreateAsync(
            provider: await PointProviderFixture.PostgreSqlAsync(barrier)
        );
        await f.Countdowns.InitializeAsync(default);
        var first = new AutomationPreparedCountdown(
            new(
                "same",
                TimeSpan.FromSeconds(20),
                CountdownOperation.Start,
                CountdownTimerOwner.Viewer
            ),
            "one",
            "Same display"
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        barrier.Arm();
        var leading = f.Countdowns.ApplyPreparedAsync(new(f.HostId), first, timeout.Token);
        var leader = await barrier.LeaderEntered.WaitAsync(timeout.Token);
        var duplicate = f.Countdowns.ApplyPreparedAsync(
            new(f.HostId),
            first with
            {
                Action = first.Action with { Duration = TimeSpan.FromSeconds(99) },
                OwnerDisplayName = "Changed",
            },
            timeout.Token
        );
        try
        {
            var contender = await barrier.ContenderAttempted.WaitAsync(timeout.Token);
            contender.Pid.ShouldNotBe(leader.Pid);
            contender.Database.ShouldBe(leader.Database);
        }
        finally
        {
            barrier.Release();
        }
        _ = (await leading).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        _ = (await duplicate).ShouldBeOfType<AutomationCountdownApplyOutcome.AlreadyRunning>();
        _ = (
            await f.Countdowns.ApplyPreparedAsync(
                new(f.HostId),
                first with
                {
                    OwnerViewerId = "two",
                },
                default
            )
        ).ShouldBeOfType<AutomationCountdownApplyOutcome.Started>();
        await using var db = await f.Database.CreateDbContextAsync();
        var timers = await db
            .AutomationCountdowns.AsNoTracking()
            .OrderBy(value => value.OwnerViewerId)
            .ToArrayAsync();
        timers.Select(value => value.OwnerViewerId).ShouldBe(["one", "two"]);
        timers.ShouldAllBe(value =>
            value.OwnerDisplayName == "Same display"
            && value.DeadlineUtc - value.StartedAtUtc == TimeSpan.FromSeconds(20)
        );
    }
}
