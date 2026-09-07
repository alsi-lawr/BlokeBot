using System.Data.Common;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Scenario_SavedReplayAndUnsavedDraftUseFixedInputsWithOnlyTraceWrites()
    {
        var writes = new ScenarioWriteGuard();
        var entropy = new CountingIntegerEntropy(99);
        await using var fixture = await RuntimeFixture.CreateAsync(
            integerEntropy: entropy,
            databaseInterceptors: [writes],
            migrateSchema: true
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":5000}""");
        var random = Node("random-number", """{"minimum":0,"maximum":100}""");
        var transform = NumberBooleanTransform("number >= 50");
        var condition = Node(
            "condition",
            """{"predicate":false}""",
            bindings: Bindings("predicate", AutomationInputBindingMode.Connected)
        );
        var send = Node("send-chat", """{"message":"saved"}""");
        var draft = Draft(
            fixture.HostId,
            [source, delay, random, transform, condition, send],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", condition),
                Edge(random, "number", transform, "number", AutomationEdgeKind.Data),
                Edge(transform, "predicate", condition, "predicate", AutomationEdgeKind.Data),
                Edge(condition, "yes", send),
            ]
        );
        var flowId = (await fixture.Flows.SaveAsync(draft, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var scenario = fixture.Scenarios.CreateDefaultFixture(draft, source.Id, 17) with
        {
            Configurations =
            [
                new(
                    random.Id,
                    random.Definition with
                    {
                        Configuration = JsonSerializer.SerializeToElement(
                            new { minimum = 75, maximum = 75 }
                        ),
                    }
                ),
            ],
        };
        var saved = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            flowId,
            null,
            "Raid rehearsal",
            scenario,
            CancellationToken.None
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var reloaded = (
            await fixture.Scenarios.ListAsync(new(fixture.HostId), flowId, CancellationToken.None)
        ).ShouldHaveSingleItem();
        AutomationScenarioSerialization
            .Serialize(reloaded.Fixture)
            .ShouldBe(AutomationScenarioSerialization.Serialize(scenario));
        writes.Armed = true;
        var first = await fixture.Scenarios.RunSavedAsync(
            new(fixture.HostId),
            flowId,
            saved.Id!.Value,
            CancellationToken.None
        );
        fixture.Clock.Advance(TimeSpan.FromDays(10));
        var restarted = new AutomationScenarioService(
            fixture.Database,
            fixture.Catalog,
            fixture.Flows,
            fixture.Clock
        );
        var second = await restarted.RunSavedAsync(
            new(fixture.HostId),
            flowId,
            saved.Id.Value,
            CancellationToken.None
        );
        var replayed = second.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var original = first.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        replayed
            .Nodes.Select(node => (node.NodeId, node.State, node.OutcomeCode, node.VirtualTimeUtc))
            .ShouldBe(
                original.Nodes.Select(node =>
                    (node.NodeId, node.State, node.OutcomeCode, node.VirtualTimeUtc)
                )
            );
        foreach (var (replayNode, originalNode) in replayed.Nodes.Zip(original.Nodes))
        {
            replayNode
                .ResolvedInputs.Select(value => (value.PortId, value.ValueType, value.DisplayValue))
                .ShouldBe(
                    originalNode.ResolvedInputs.Select(value =>
                        (value.PortId, value.ValueType, value.DisplayValue)
                    )
                );
        }
        var completed = first.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        completed
            .Nodes.Select(node => node.NodeId)
            .ShouldBe([source.Id, delay.Id, condition.Id, send.Id]);
        completed.Nodes[^1].VirtualTimeUtc.ShouldBe(scenario.ClockUtc.AddSeconds(5));
        var unsaved = draft with
        {
            Id = flowId,
            Nodes =
            [
                .. draft.Nodes.Select(node =>
                    node.Id == send.Id
                        ? node with
                        {
                            Definition = send.Definition with
                            {
                                Configuration = JsonSerializer.SerializeToElement(
                                    new { message = "unsaved" }
                                ),
                            },
                        }
                        : node
                ),
            ],
        };
        var draftResult = (
            await restarted.RunAsync(unsaved, scenario, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        draftResult
            .Nodes[^1]
            .ResolvedInputs.ShouldHaveSingleItem()
            .DisplayValue.ShouldBe("unsaved");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (
            await db.AutomationFlowNodes.SingleAsync(node => node.Id == send.Id.Value)
        ).ConfigurationJson.ShouldContain("saved");
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
        writes.Writes.ShouldBe(0);
        writes.TraceWrites.ShouldBeGreaterThan(0);
        entropy.Calls.ShouldBe(0);
        fixture.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_DeclaredOverlayEffectDoesNotResolveLiveConfigurationOrAdmitEffects()
    {
        var overlay = new ForbiddenScenarioOverlay();
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlay,
            hostFeatures: HostFeatureFlags.Automations | HostFeatureFlags.Overlays,
            databaseInterceptors: [writes]
        );
        var source = Node("follow", "{}");
        var cue = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{Guid.NewGuid()}}","cue-id":"{{Guid.NewGuid()}}"}"""
        );
        var draft = Draft(fixture.HostId, [source, cue], [Edge(source, "flow", cue)]);
        writes.Armed = true;
        var completed = (
            await fixture.Scenarios.RunDefaultAsync(draft, source.Id, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        completed.Nodes[^1].State.ShouldBe(AutomationNodeRunState.Succeeded);
        var failedFixture = fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
        {
            Effects = [new(cue.Id, AutomationScenarioEffectResult.Failed)],
        };
        (await fixture.Scenarios.RunAsync(draft, failedFixture, CancellationToken.None))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
            .Nodes[^1]
            .State.ShouldBe(AutomationNodeRunState.Failed);
        overlay.Calls.ShouldBe(0);
        writes.Writes.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_AuthoringIsFlowAndHostBoundedAndDeletionReleasesCapacity()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("follow", "{}");
        var send = Node("send-chat", """{"message":"hello"}""");
        var draft = Draft(fixture.HostId, [source, send], [Edge(source, "flow", send)]);
        var flow = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        var other = await fixture.SaveAsync([Node("follow", "{}")], []);
        var input = fixture.Scenarios.CreateDefaultFixture(draft, source.Id);
        var first = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            flow,
            null,
            "Original",
            input,
            CancellationToken.None
        );
        var id = first.Id!.Value;
        var tooLarge = input with
        {
            Context = input.Context with
            {
                Actor = input.Context.Actor! with
                {
                    DisplayName = new string('x', AutomationScenarioService.MaximumFixtureBytes),
                },
            },
        };
        (
            await fixture.Scenarios.SaveAsync(
                new(fixture.HostId),
                flow,
                id,
                "Rejected",
                tooLarge,
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Invalid);
        var malformed = input with
        {
            Context = input.Context with
            {
                Variables = new(
                    new Dictionary<AutomationVariableName, AutomationVariable>
                    {
                        [new("invalid")] = new(
                            new AutomationValue.Nil(),
                            AutomationDataSensitivity.Safe
                        ),
                    }
                ),
            },
        };
        (
            await fixture.Scenarios.SaveAsync(
                new(fixture.HostId),
                flow,
                id,
                "Rejected",
                malformed,
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Invalid);
        var preserved = (
            await fixture.Scenarios.ListAsync(new(fixture.HostId), flow, CancellationToken.None)
        ).ShouldHaveSingleItem();
        preserved.Name.ShouldBe("Original");
        AutomationScenarioSerialization
            .Serialize(preserved.Fixture)
            .ShouldBe(AutomationScenarioSerialization.Serialize(input));
        (
            await fixture.Scenarios.RenameAsync(
                new(fixture.HostId + 1),
                flow,
                id,
                "foreign",
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.NotFound);
        (
            await fixture.Scenarios.DeleteAsync(
                new(fixture.HostId),
                other,
                id,
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.NotFound);
        (
            await fixture.Scenarios.SaveAsync(
                new(fixture.HostId),
                other,
                id,
                "foreign",
                input,
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.NotFound);
        (
            await fixture.Scenarios.RenameAsync(
                new(fixture.HostId),
                flow,
                id,
                "Renamed",
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var copy = await fixture.Scenarios.DuplicateAsync(
            new(fixture.HostId),
            flow,
            id,
            "Copy",
            CancellationToken.None
        );
        copy.Id.ShouldNotBe(id);
        for (var i = 2; i < AutomationScenarioService.MaximumScenariosPerFlow; i++)
        {
            (
                await fixture.Scenarios.DuplicateAsync(
                    new(fixture.HostId),
                    flow,
                    id,
                    $"Copy {i}",
                    CancellationToken.None
                )
            ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        }
        (
            await fixture.Scenarios.DuplicateAsync(
                new(fixture.HostId),
                flow,
                id,
                "Over limit",
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.LimitReached);
        (
            await fixture.Scenarios.DeleteAsync(
                new(fixture.HostId),
                flow,
                copy.Id!.Value,
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Deleted);
        (
            await fixture.Scenarios.DuplicateAsync(
                new(fixture.HostId),
                flow,
                id,
                "Replacement",
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        (await fixture.Scenarios.ListAsync(new(fixture.HostId), flow, CancellationToken.None))
            .Single(item => item.Id == id)
            .Name.ShouldBe("Renamed");
        (
            await fixture.Scenarios.ListAsync(new(fixture.HostId + 1), flow, CancellationToken.None)
        ).ShouldBeEmpty();
    }

    [Test]
    public async Task Scenario_ConnectedFixturesPreserveSensitivityAndStopContinueNeverRetry()
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var source = Node("follow", "{}");
        var random = Node("random-number", """{"minimum":0,"maximum":100}""");
        var transform = NumberBooleanTransform("number >= 50");
        var condition = Node(
            "condition",
            """{"predicate":false}""",
            bindings: Bindings("predicate", AutomationInputBindingMode.Connected)
        );
        var send = Node("send-chat", """{"message":"hello"}""") with
        {
            FailurePolicy = AutomationNodeFailurePolicy.Continue,
        };
        var final = Node("send-chat", """{"message":"final"}""");
        var draft = Draft(
            fixture.HostId,
            [source, random, transform, condition, send, final],
            [
                Edge(source, "flow", condition),
                Edge(random, "number", transform, "number", AutomationEdgeKind.Data),
                Edge(transform, "predicate", condition, "predicate", AutomationEdgeKind.Data),
                Edge(condition, "yes", send),
                Edge(send, "complete", final),
            ]
        );
        var input = fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
        {
            ConnectedInputs =
            [
                new(
                    condition.Id,
                    new("predicate"),
                    new(new AutomationValue.Boolean(true), AutomationDataSensitivity.Safe)
                ),
            ],
            Effects = [new(send.Id, AutomationScenarioEffectResult.Failed)],
        };
        writes.Armed = true;
        var completed = (
            await fixture.Scenarios.RunAsync(draft, input, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        completed
            .Nodes.Select(node => node.NodeId)
            .ShouldBe([source.Id, condition.Id, send.Id, final.Id]);
        completed.Nodes[2].State.ShouldBe(AutomationNodeRunState.ContinuedAfterFailure);
        var stopped = draft with
        {
            Nodes =
            [
                .. draft.Nodes.Select(node =>
                    node.Id == send.Id
                        ? node with
                        {
                            FailurePolicy = AutomationNodeFailurePolicy.Stop,
                        }
                        : node
                ),
            ],
        };
        (await fixture.Scenarios.RunAsync(stopped, input, CancellationToken.None))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
            .Nodes[^1]
            .NodeId.ShouldBe(send.Id);
        var sensitive = input with
        {
            ConnectedInputs =
            [
                input.ConnectedInputs[0] with
                {
                    Value = new(
                        new AutomationValue.Boolean(true),
                        AutomationDataSensitivity.Sensitive
                    ),
                },
            ],
        };
        _ = (
            await fixture.Scenarios.RunAsync(draft, sensitive, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        var stale = input with { SourceSchemaVersion = new(999) };
        _ = (
            await fixture.Scenarios.RunAsync(draft, stale, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        fixture.Chat.Calls.ShouldBe(0);
        writes.Writes.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_RejectsUnsupportedLaterNodesBeforeEvaluationAndCancellationWritesOnlyTrace()
    {
        var writes = new ScenarioWriteGuard();
        var display = DisplayNameHandler();
        using var cancellation = new CancellationTokenSource();
        await using var fixture = await RuntimeFixture.CreateAsync(
            handlers: [new CancellingScenarioHandler(display, cancellation)],
            databaseInterceptors: [writes]
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var producer = Node(
            "test-display-name-transform",
            "{}",
            bindings: Bindings("actor", AutomationInputBindingMode.Connected)
        );
        var supported = Node(
            "send-chat",
            """{"message":"hello"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var unsupported = Node("test-text-consumer", """{"message":"not simulated"}""");
        var draft = Draft(
            fixture.HostId,
            [source, producer, supported, unsupported],
            [
                Edge(source, "flow", supported),
                Edge(supported, "complete", unsupported),
                Edge(source, "actor", producer, "actor", AutomationEdgeKind.Data),
                Edge(producer, "value", supported, "message", AutomationEdgeKind.Data),
            ]
        );
        writes.Armed = true;
        var result = (
            await fixture.Scenarios.RunDefaultAsync(draft, source.Id, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        result.Errors.ShouldContain(error =>
            error.NodeId == unsupported.Id && error.Code == "scenario-simulation-unsupported"
        );
        display.Calls.ShouldBe(0);
        await using (var rejectedDb = await fixture.Database.CreateDbContextAsync())
        {
            (await rejectedDb.AutomationTraces.CountAsync()).ShouldBe(0);
        }
        var admitted = draft with
        {
            Nodes = [.. draft.Nodes.Where(node => node.Id != unsupported.Id)],
            Edges = [.. draft.Edges.Where(edge => edge.TargetNodeId != unsupported.Id)],
        };
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Scenarios.RunDefaultAsync(admitted, source.Id, cancellation.Token)
        );
        display.Calls.ShouldBe(1);
        writes.Writes.ShouldBe(0);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var storedTrace = (
            await new AutomationTraceStore(fixture.Database, fixture.Clock).ListAsync(
                new(fixture.HostId),
                null,
                CancellationToken.None
            )
        ).ShouldHaveSingleItem();
        var trace = await ReadTraceAsync(fixture, storedTrace.Id);
        trace.Events.ShouldContain(entry =>
            entry.Event.Kind == AutomationTraceEventKind.Cancellation
        );
        trace.Events[^1].Event.Outcome.ShouldBe(AutomationTraceOutcome.Cancelled);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
    }

    private sealed class ForbiddenScenarioOverlay : IOverlayCueAdmissionService
    {
        internal int Calls { get; private set; }

        private Exception Called()
        {
            Calls++;
            return new InvalidOperationException("Scenario contacted the live overlay provider.");
        }

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken,
            BlokeBotDbContext? preparationDb = null
        ) => throw Called();

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => throw Called();

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => throw Called();
    }

    private sealed class CancellingScenarioHandler(
        IAutomationPureNodeHandler inner,
        CancellationTokenSource cancellation
    ) : IAutomationPureNodeHandler
    {
        public AutomationPureHandlerContract Contract => inner.Contract;

        public async ValueTask<AutomationPureNodeResult> ExecuteAsync(
            AutomationPureNodeInput input,
            CancellationToken cancellationToken
        )
        {
            var result = await inner.ExecuteAsync(input, cancellationToken);
            cancellation.Cancel();
            return result;
        }
    }

    private sealed class ScenarioWriteGuard : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal int Writes { get; private set; }
        internal int TraceWrites { get; private set; }

        private void Check(DbCommand command)
        {
            if (
                Armed
                && !command
                    .CommandText.TrimStart()
                    .StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (
                    command.CommandText.StartsWith(
                        "UPDATE hosts SET EnabledFeatures = EnabledFeatures WHERE Id = ",
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }
                var statements = command.CommandText.Split(
                    ';',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                );
                if (
                    statements.All(statement =>
                        statement.StartsWith(
                            "INSERT INTO \"automation_traces\" ",
                            StringComparison.Ordinal
                        )
                        || statement.StartsWith(
                            "INSERT INTO \"automation_trace_events\" ",
                            StringComparison.Ordinal
                        )
                        || statement.StartsWith(
                            "UPDATE \"automation_traces\" ",
                            StringComparison.Ordinal
                        )
                    )
                )
                {
                    TraceWrites++;
                    return;
                }
                Writes++;
                throw new InvalidOperationException(
                    "Scenario attempted a production database mutation."
                );
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default
        )
        {
            Check(command);
            return ValueTask.FromResult(result);
        }
    }
}
