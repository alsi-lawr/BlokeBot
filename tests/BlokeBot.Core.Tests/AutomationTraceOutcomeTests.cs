using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task TraceOutcome_PureProducerAndContinuedConsumerUseActualLiveAndScenarioFacts(
        bool scenario,
        bool fails
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var producer = CelTimerParse("1", fails ? "args[99]" : "'private-preview'");
        var consumer = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        ) with
        {
            FailurePolicy = AutomationNodeFailurePolicy.Continue,
        };
        var draft = Draft(
            fixture.HostId,
            [source, producer, consumer],
            [
                Edge(source, "flow", consumer),
                Edge(producer, "name", consumer, "message", AutomationEdgeKind.Data),
            ]
        );
        AutomationTraceId id;
        if (scenario)
        {
            var result = (
                await fixture.Scenarios.RunDefaultAsync(draft, source.Id, default)
            ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
            id = result.TraceId;
            fixture.Chat.Calls.ShouldBe(0);
        }
        else
        {
            _ = (
                await fixture.Flows.SaveAsync(draft, default)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            var dispatch = await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            );
            id = new(dispatch.RunIds.ShouldHaveSingleItem().Value);
            fixture.Chat.Calls.ShouldBe(fails ? 0 : 1);
        }
        var trace = await ReadTraceAsync(fixture, id);
        trace
            .Events.Count(entry =>
                entry.Event.Node?.Id == producer.Id
                && entry.Event.Kind == AutomationTraceEventKind.Outputs
            )
            .ShouldBe(1);
        trace.OutcomeSummary.ShouldBe(new(AutomationTraceOutcome.Succeeded, fails));
        var projection = AutomationTracePresentation.Create(trace);
        projection
            .RootOutcomes.Single(outcome => outcome.NodeId == producer.Id)
            .State.ShouldBe(
                fails ? AutomationNodeRunState.Failed : AutomationNodeRunState.Succeeded
            );
        projection
            .RootOutcomes.Single(outcome => outcome.NodeId == consumer.Id)
            .State.ShouldBe(
                fails
                    ? AutomationNodeRunState.ContinuedAfterFailure
                    : AutomationNodeRunState.Succeeded
            );
        projection.Status.ShouldBe(fails ? "Completed with failures" : "Completed");
        JsonSerializer.Serialize(trace).ShouldNotContain("private-preview");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TraceOutcome_NestedFailureKeepsItsCausalCallerWithoutBecomingRootFailure(
        bool scenario
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync(chatAdmissions: [false]);
        var service = Subflows(fixture);
        var subflow = Subflow(fixture.HostId);
        var failedLeaf = Node(
            "send-chat",
            """{"message":"private-leaf"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var nestedProducer = Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "text",
                        "text",
                        "Text",
                        "text-input",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "private-invalid"
                    ),
                ],
                [
                    new(
                        "message",
                        "Message",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "string(decimal(text))"
                    ),
                ]
            ),
            bindings: Bindings("text-input", AutomationInputBindingMode.Fixed)
        );
        subflow = subflow with
        {
            Graph = subflow.Graph with
            {
                Nodes =
                [
                    subflow.Graph.Nodes[0],
                    failedLeaf,
                    nestedProducer,
                    subflow.Graph.Nodes[1],
                ],
                Edges =
                [
                    Edge(subflow.Graph.Nodes[0], "complete", failedLeaf),
                    Edge(failedLeaf, "complete", subflow.Graph.Nodes[1]),
                    Edge(nestedProducer, "message", failedLeaf, "message", AutomationEdgeKind.Data),
                ],
            },
        };
        var published = await PublishReviewedAsync(service, subflow, default);
        var revision = published
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Published>()
            .Revision;
        var draft = Caller(fixture.HostId, revision);
        draft = draft with
        {
            Nodes = draft.Nodes.SetItem(
                1,
                draft.Nodes[1] with
                {
                    FailurePolicy = AutomationNodeFailurePolicy.Continue,
                }
            ),
        };
        AutomationTraceId id;
        if (scenario)
        {
            var defaults = fixture.Scenarios.CreateDefaultFixture(draft, draft.Nodes[0].Id);
            var result = (
                await fixture.Scenarios.RunAsync(draft, defaults, default)
            ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
            id = result.TraceId;
        }
        else
        {
            _ = (
                await fixture.Flows.SaveAsync(draft, default)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            id = new(
                (
                    await fixture.Runtime.DispatchAsync(
                        new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                        default
                    )
                )
                    .RunIds.ShouldHaveSingleItem()
                    .Value
            );
        }
        var trace = await ReadTraceAsync(fixture, id);
        var presentation = AutomationTracePresentation.Create(trace);
        var failed = presentation.Rows.Single(row =>
            row.Entry.Event.Node?.Id == failedLeaf.Id
            && row.Entry.Event.Kind == AutomationTraceEventKind.Result
        );
        failed.RootNodeId.ShouldBe(draft.Nodes[1].Id);
        failed.RevisionId.ShouldBe(revision.Id);
        _ = failed.Entry.Event.Invocation.ParentId.ShouldNotBeNull();
        presentation.OutcomeSummary.ShouldBe(new(AutomationTraceOutcome.Succeeded, true));
        presentation.Status.ShouldBe("Completed with failures");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TraceOutcome_FirstDroppedFailureStillUpdatesSummaryAndExpiredAppendCannotResurrect(
        bool byteLimit
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        await using var db = await fixture.Database.CreateDbContextAsync();
        await AutomationTraceStore.CreateAsync(db, id, fixture.HostId, null, null, now, default);
        var filler = AutomationTraceRedaction.Event(id, AutomationTraceEventKind.Resume, now);
        if (byteLimit)
        {
            filler = filler with
            {
                Values =
                [
                    .. Enumerable
                        .Range(0, 200)
                        .Select(index => new AutomationTraceValue(
                            new($"port-{index}"),
                            AutomationPortValueType.Text,
                            [AutomationValueProvenance.Generated],
                            []
                        )),
                ],
            };
        }
        var failure = TraceFailure(id, now) with { Values = filler.Values };
        var failureBytes = Encoding.UTF8.GetByteCount(
            JsonSerializer.Serialize(failure, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        while (true)
        {
            var header = await db
                .AutomationTraces.AsNoTracking()
                .SingleAsync(trace => trace.Id == id);
            if (
                header.EventCount == AutomationTraceStore.MaximumEvents
                || header.ByteCount + failureBytes > AutomationTraceStore.MaximumBytes
            )
            {
                break;
            }
            await AutomationTraceStore.AppendAsync(db, id, filler, now, default);
        }
        var prefix = await ReadTraceAsync(fixture, new(id));
        prefix.Truncation.ShouldBe(AutomationTraceTruncation.None);
        await AutomationTraceStore.AppendAsync(db, id, failure, now, default);
        await AutomationTraceStore.AppendAsync(
            db,
            id,
            AutomationTraceRedaction.Event(
                id,
                AutomationTraceEventKind.Terminal,
                now,
                outcome: AutomationTraceOutcome.Succeeded
            ),
            now,
            default
        );
        var completed = await ReadTraceAsync(fixture, new(id));
        JsonSerializer
            .Serialize(completed.Events)
            .ShouldBe(JsonSerializer.Serialize(prefix.Events));
        completed.ByteCount.ShouldBe(prefix.ByteCount);
        completed.Truncation.ShouldBe(
            byteLimit ? AutomationTraceTruncation.ByteLimit : AutomationTraceTruncation.EventLimit
        );
        completed.OutcomeSummary.ShouldBe(new(AutomationTraceOutcome.Succeeded, true));
        var store = new AutomationTraceStore(fixture.Database, fixture.Clock);
        (await store.ListAsync(new(fixture.HostId), null, default))
            .ShouldHaveSingleItem()
            .OutcomeSummary.ShouldBe(completed.OutcomeSummary);
        fixture.Clock.Advance(AutomationTraceStore.Retention);
        await AutomationTraceStore.AppendAsync(
            db,
            id,
            failure,
            fixture.Clock.GetUtcNow().UtcDateTime,
            default
        );
        var expired = await db.AutomationTraces.AsNoTracking().SingleAsync(trace => trace.Id == id);
        expired.EventCount.ShouldBe(prefix.Events.Length);
        expired.ExpiresAtUtc.ShouldBe(prefix.ExpiresAtUtc.UtcDateTime);
        (await store.DeleteExpiredAsync(default)).ShouldBe(1);
        await AutomationTraceStore.AppendAsync(db, id, failure, now, default);
        (await db.AutomationTraces.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task TraceOutcome_SqliteUpgradePreservesOldJsonAndUnknownSummary()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(
            AutomationTraceRedaction.Event(
                id,
                AutomationTraceEventKind.Terminal,
                now,
                outcome: AutomationTraceOutcome.Succeeded
            ),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        int hostId;
        await using (var old = await database.CreateDbContextAsync())
        {
            await old.Database.MigrateAsync("20261015205956_CustomCommandArguments");
            var host = new BotHost { Login = "trace-upgrade", CreatedAtUtc = now };
            _ = old.Hosts.Add(host);
            _ = await old.SaveChangesAsync();
            hostId = host.Id;
            _ = await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automation_traces (\"Id\",\"HostId\",\"SchemaVersion\",\"CreatedAtUtc\",\"ExpiresAtUtc\",\"EventCount\",\"ByteCount\",\"Truncation\") VALUES ({id},{hostId},{1},{now},{now.AddDays(7)},{1},{Encoding.UTF8.GetByteCount(json)},{0})"
            );
            _ = await old.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO automation_trace_events (\"TraceId\",\"Sequence\",\"EventJson\") VALUES ({id},{1},{json})"
            );
        }
        await new BlokeBotDatabaseInitializer(database).InitializeAsync(default);
        await using var reopened = await database.CreateDbContextAsync();
        (await reopened.AutomationTraceEvents.SingleAsync()).EventJson.ShouldBe(json);
        var header = await reopened.AutomationTraces.SingleAsync();
        header.TerminalOutcome.ShouldBeNull();
        header.ObservedFailures.ShouldBeNull();
        var snapshot = (
            await new AutomationTraceStore(database, TimeProvider.System).ReadAsync(
                new(hostId),
                new(id),
                default
            )
        )
            .ShouldBeOfType<AutomationTraceReadOutcome.Available>()
            .Trace;
        AutomationTracePresentation
            .Create(snapshot)
            .Status.ShouldBe("Completed · failure history unknown");
        using var ui = new BunitContext();
        var panel = ui.Render<AutomationTracePanel>(parameters =>
            parameters
                .Add(component => component.Snapshot, snapshot)
                .Add(
                    component => component.Presentation,
                    AutomationTracePresentation.Create(snapshot)
                )
        );
        panel.Find("h2").TextContent.ShouldContain("failure history unknown");
        await AutomationTraceStore.AppendAsync(
            reopened,
            id,
            AutomationTraceRedaction.Event(
                id,
                AutomationTraceEventKind.Terminal,
                now,
                outcome: AutomationTraceOutcome.Succeeded
            ),
            now,
            default
        );
        (
            await reopened.AutomationTraces.AsNoTracking().SingleAsync()
        ).ObservedFailures.ShouldBeNull();
        await AutomationTraceStore.AppendAsync(reopened, id, TraceFailure(id, now), now, default);
        (await reopened.AutomationTraces.AsNoTracking().SingleAsync()).ObservedFailures.ShouldBe(
            true
        );
    }

    [Test]
    [Arguments(AutomationTraceOutcome.Succeeded)]
    [Arguments(AutomationTraceOutcome.Failed)]
    [Arguments(AutomationTraceOutcome.Invalidated)]
    [Arguments(AutomationTraceOutcome.Cancelled)]
    public async Task TraceOutcome_OnlyRootTerminalControlsCompletionAndFailureEvidenceStaysPositive(
        AutomationTraceOutcome terminal
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var id = Guid.NewGuid();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        await AutomationTraceStore.CreateAsync(db, id, fixture.HostId, null, null, now, default);
        foreach (
            var kind in new[]
            {
                AutomationTraceEventKind.Recovery,
                AutomationTraceEventKind.Resume,
                AutomationTraceEventKind.Cancellation,
            }
        )
        {
            await AutomationTraceStore.AppendAsync(
                db,
                id,
                AutomationTraceRedaction.Event(
                    id,
                    kind,
                    now,
                    outcome: AutomationTraceOutcome.Interrupted
                ),
                now,
                default
            );
        }
        (await ReadTraceAsync(fixture, new(id))).OutcomeSummary.ShouldBe(new(null, false));
        await AutomationTraceStore.AppendAsync(db, id, TraceFailure(id, now), now, default);
        var nestedTerminal = AutomationTraceRedaction.Event(
            id,
            AutomationTraceEventKind.Terminal,
            now,
            outcome: AutomationTraceOutcome.Failed
        ) with
        {
            Invocation = new(Guid.NewGuid(), id),
        };
        await AutomationTraceStore.AppendAsync(db, id, nestedTerminal, now, default);
        var pending = AutomationTracePresentation.Create(await ReadTraceAsync(fixture, new(id)));
        pending.OutcomeSummary.ShouldBe(new(null, true));
        pending.Status.ShouldBe("Failures observed · completion unknown");
        await AutomationTraceStore.AppendAsync(
            db,
            id,
            AutomationTraceRedaction.Event(
                id,
                AutomationTraceEventKind.Terminal,
                now,
                outcome: terminal
            ),
            now,
            default
        );
        var completed = AutomationTracePresentation.Create(await ReadTraceAsync(fixture, new(id)));
        completed.OutcomeSummary.ShouldBe(new(terminal, true));
        completed.HasUnsuccessfulOutcome.ShouldBeTrue();
        completed.Status.ShouldBe(
            terminal == AutomationTraceOutcome.Succeeded
                ? "Completed with failures"
                : terminal.ToString()
        );
    }

    [Test, Explicit]
    public async Task TraceOutcome_PostgreSqlUpgradeAndTransactionalSummaryUseTheExistingIsolatedFixture()
    {
        await using var fixture = await PointProviderFixture.PostgreSqlAsync();
        await using var initial = fixture.CreateDbContext();
        var connection = initial.Database.GetConnectionString()!;
        await using var db = new BlokeBotDbContext(
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseNpgsql(
                    connection,
                    options => options.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
                )
                .Options
        );
        // The fixture owns this uniquely named disposable database; replace only its EnsureCreated schema.
        _ = await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync("20261015210035_CustomCommandArguments");
        var host = new BotHost { Login = "trace-pg-upgrade", CreatedAtUtc = DateTime.UtcNow };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(
            AutomationTraceRedaction.Event(id, AutomationTraceEventKind.Resume, now),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO automation_traces (\"Id\",\"HostId\",\"SchemaVersion\",\"CreatedAtUtc\",\"ExpiresAtUtc\",\"EventCount\",\"ByteCount\",\"Truncation\") VALUES ({id},{host.Id},{1},{now},{now.AddDays(7)},{1},{Encoding.UTF8.GetByteCount(json)},{0})"
        );
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO automation_trace_events (\"TraceId\",\"Sequence\",\"EventJson\") VALUES ({id},{1},{json})"
        );
        await db.Database.MigrateAsync();
        var legacy = await db.AutomationTraces.AsNoTracking().SingleAsync();
        legacy.TerminalOutcome.ShouldBeNull();
        legacy.ObservedFailures.ShouldBeNull();
        (await db.AutomationTraceEvents.AsNoTracking().SingleAsync()).EventJson.ShouldBe(json);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await AutomationTraceStore.AppendAsync(db, id, TraceFailure(id, now), now, default);
            await transaction.RollbackAsync();
        }
        (await db.AutomationTraces.AsNoTracking().SingleAsync()).ObservedFailures.ShouldBeNull();
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async index =>
                {
                    await using var writer = fixture.CreateDbContext();
                    await AutomationTraceStore.AppendAsync(
                        writer,
                        id,
                        index == 0
                            ? TraceFailure(id, now)
                            : AutomationTraceRedaction.Event(
                                id,
                                AutomationTraceEventKind.Terminal,
                                now,
                                outcome: AutomationTraceOutcome.Succeeded
                            ),
                        now,
                        default
                    );
                })
        );
        var trace = (
            await new AutomationTraceStore(fixture, TimeProvider.System).ReadAsync(
                new(host.Id),
                new(id),
                default
            )
        )
            .ShouldBeOfType<AutomationTraceReadOutcome.Available>()
            .Trace;
        trace.OutcomeSummary.ShouldBe(new(AutomationTraceOutcome.Succeeded, true));
        trace.Events.Select(entry => entry.Sequence).ShouldBe(Enumerable.Range(1, 9));
        trace.Events[0].Event.Kind.ShouldBe(AutomationTraceEventKind.Resume);
        JsonSerializer.Serialize(trace).ShouldNotContain("private");
    }

    [Test]
    public async Task TraceOutcome_CurrentPreviewMergesPureFactsButNeverLeaksIntoHistoryOrEditedDraft()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var producer = CelTimerParse("1", "'private-preview'");
        var action = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        ) with
        {
            FailurePolicy = AutomationNodeFailurePolicy.Continue,
        };
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var draft = Draft(
            fixture.HostId,
            [source, delay, producer, action],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", action),
                Edge(producer, "name", action, "message", AutomationEdgeKind.Data),
            ]
        );
        _ = (
            await fixture.Flows.SaveAsync(draft, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        using var ui = UiTestContextFactory.Create(
            (SqliteBlokeBotDbFactory)fixture.Database,
            fixture.HostId
        );
        _ = ui.Services.AddSingleton<IPublicChatMessageSender>(fixture.Chat);
        _ = ui.Services.AddSingleton<IOverlayCueAdmissionService>(new NoOverlayCues());
        _ = ui.Services.AddSingleton<TimeProvider>(fixture.Clock);
        _ = ui.Services.AddBlokeBotAutomations();
        _ = ui.Services.AddSingleton(fixture.Scenarios);
        var page = ui.Render<AutomationEditorPage>();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.Count.ShouldBe(4)
        );
        page.Find("[data-automation-test-flow]").Click();
        page.Find(".automation-effect-choice select").Change("Failed");
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () => page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
            TimeSpan.FromSeconds(20)
        );
        page.FindComponent<AutomationTracePanel>()
            .Instance.Selected!.RootNodeId.ShouldBe(action.Id);
        page.FindComponent<AutomationTracePanel>()
            .Instance.Presentation!.Status.ShouldBe("Completed with failures");
        var first = page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id;
        var rich = page.FindComponent<AutomationFlowCanvas>()
            .Instance.SampleOutcomes.Single(outcome => outcome.NodeId == action.Id);
        rich.VirtualTimeUtc.ShouldNotBe(default);
        rich.ResolvedInputs.ShouldNotBeEmpty();
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.SampleOutcomes.Single(outcome => outcome.NodeId == producer.Id)
            .State.ShouldBe(AutomationNodeRunState.Succeeded);
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () =>
                page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id.ShouldNotBe(first),
            TimeSpan.FromSeconds(20)
        );
        var current = page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id;
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationTracePanel>()
                .Instance.SelectRun.InvokeAsync(first.Value.ToString())
        );
        var historical = page.FindComponent<AutomationFlowCanvas>()
            .Instance.SampleOutcomes.Single(outcome => outcome.NodeId == action.Id);
        historical.VirtualTimeUtc.ShouldBe(default);
        historical.ResolvedInputs.IsDefaultOrEmpty.ShouldBeTrue();
        page.FindComponent<AutomationRunDrawer>().Instance.Kind.ShouldBe("history");
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationTracePanel>()
                .Instance.SelectRun.InvokeAsync(current.Value.ToString())
        );
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.SampleOutcomes.Single(outcome => outcome.NodeId == action.Id)
            .ResolvedInputs.IsDefaultOrEmpty.ShouldBeTrue();
        page.FindComponent<AutomationRunDrawer>().Instance.Kind.ShouldBe("history");
        // Missing historical records also cannot borrow the current result.
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationTracePanel>()
                .Instance.SelectRun.InvokeAsync(Guid.NewGuid().ToString())
        );
        page.FindComponent<AutomationFlowCanvas>().Instance.SampleOutcomes.ShouldBeEmpty();
        page.FindComponent<AutomationRunDrawer>().Instance.Kind.ShouldBe("history");
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationTracePanel>()
                .Instance.SelectRun.InvokeAsync(current.Value.ToString())
        );
        page.Find("#automation-flow-name").Input("Edited draft");
        page.FindComponent<AutomationRunDrawer>().Instance.Kind.ShouldBe("history");
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.SampleOutcomes.ShouldAllBe(outcome =>
                outcome.ResolvedInputs.IsDefaultOrEmpty && outcome.VirtualTimeUtc == default
            );
        fixture.Chat.Calls.ShouldBe(0);
    }

    private static AutomationTraceEventData TraceFailure(Guid id, DateTime now) =>
        AutomationTraceRedaction.Event(
            id,
            AutomationTraceEventKind.Outputs,
            now,
            outcome: AutomationTraceOutcome.Failed
        ) with
        {
            Node = new(
                new(Guid.NewGuid()),
                AutomationDefinitionIds.CelTransform,
                new(1),
                AutomationNodeFailurePolicy.Stop
            ),
        };
}
