using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommandArguments_FrozenTypedPortsKeepValuesAndDueAfterSchemaRemoval(
        bool restart
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var graph = TypedCommandTimer(f.HostId);
        var saved = (
            await f.Flows.SaveAsync(graph.Draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var context = TypedCommandContext(f.HostId, "1.001 \"cup  of\"\ttea  ");
        var start = f.Clock.GetUtcNow();
        var run = (
            await f.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        f.Chat.Messages.ShouldBe(["\"cup  of\"\ttea  "]);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var state = await db.AutomationFlowRuns.SingleAsync();
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(state.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>()
                .Flow;
            frozen
                .Nodes.Single(node => node.Id == graph.Source.Id.Value)
                .Contract.ShouldNotBeNull()
                .Outputs.Single(port => port.Id.Value == "argument.seconds")
                .ValueType.ShouldBe(AutomationPortValueType.Number);
            var restored = AutomationRuntimeSerialization
                .RestoreContext(state.ContextSchemaVersion, state.ContextJson)
                .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
                .Context;
            restored
                .CommandArguments.ShouldNotBeNull()
                .Values["name"]
                .ShouldBe(context.CommandArguments!.Values["name"]);
            restored.Arguments.ShouldBe(context.Arguments);
            restored
                .Variables.SafeForExternalUse()
                .Values.ShouldNotContain(value => value == context.CommandArguments.Values["name"]);
            (
                await db.AutomationNodeRuns.SingleAsync(node =>
                    node.NodeId == graph.Finish.Id.Value
                )
            ).AvailableAtUtc.ShouldBe(start.UtcDateTime.AddMilliseconds(1001));
        }
        // Repair the saved graph through its normal save owner, then remove the no-longer-wired schema.
        var simple = Node("send-chat", """{"message":"new graph"}""");
        _ = (
            await f.Flows.SaveAsync(
                Draft(f.HostId, [graph.Source, simple], [Edge(graph.Source, "flow", simple)]) with
                {
                    Id = saved.FlowId,
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        (await SaveArgumentSchema(f, null)).ShouldBeNull();
        var runtime = restart ? f.NewRuntime() : f.Runtime;
        f.Clock.Advance(TimeSpan.FromTicks((1001 * TimeSpan.TicksPerMillisecond) - 1));
        await runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.Count.ShouldBe(1);
        f.Clock.Advance(TimeSpan.FromTicks(1));
        await runtime.ResumeDueAsync(CancellationToken.None);
        await runtime.ResumeDueAsync(CancellationToken.None);
        f.Chat.Messages.ShouldBe(["\"cup  of\"\ttea  ", "\"cup  of\"\ttea  "]);
        (await runtime.ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        var trace = await ReadTraceAsync(f, new(run.Value));
        JsonSerializer.Serialize(trace).ShouldNotContain("cup  of");
        trace
            .Events.Where(item => item.Event.Kind == AutomationTraceEventKind.Outputs)
            .SelectMany(item => item.Event.Values)
            .ShouldContain(value =>
                value.Provenance.Contains(AutomationValueProvenance.PublicChat)
            );
    }

    [Test]
    public async Task CommandArguments_SavedDisabledDependenciesRefuseOnlyStructuralBreaksAndFenceStalePreparation()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var graph = TypedCommandTimer(f.HostId);
        (await f.Flows.ValidateAsync(graph.Draft, default)).Errors.ShouldBeEmpty();
        (await SaveArgumentSchema(f, null)).ShouldBeNull();
        _ = (
            await f.Flows.SaveAsync(graph.Draft, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var saved = (
            await f.Flows.SaveAsync(graph.Draft with { IsEnabled = false }, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        foreach (
            var schema in new CustomCommandArgumentSchema?[]
            {
                null,
                new([
                    new("seconds", CustomCommandArgumentKind.Number, 0),
                    new("renamed", CustomCommandArgumentKind.RemainingText),
                ]),
                new([
                    new("seconds", CustomCommandArgumentKind.Token),
                    new("name", CustomCommandArgumentKind.RemainingText),
                ]),
            }
        )
        {
            var failure = (
                await SaveArgumentSchema(f, schema)
            ).ShouldBeOfType<CustomCommandConfigurationSaveFailure.ArgumentDependency>();
            failure.FlowId.ShouldBe(saved.FlowId.Value);
            failure.SourceNodeId.ShouldBe(graph.Source.Id.Value);
        }
        var prepared = TypedCommandContext(f.HostId, "1 tea");
        var changed = CustomCommandArgumentTests.TimerSchema with
        {
            Fields = CustomCommandArgumentTests.TimerSchema.Fields.Add(
                new("extra", CustomCommandArgumentKind.Token)
            ),
        };
        // Remaining text must stay last; a new token before it retains all wired port identities.
        changed = changed with
        {
            Fields = [changed.Fields[0], changed.Fields[2], changed.Fields[1]],
        };
        (await SaveArgumentSchema(f, changed)).ShouldBeNull();
        var dispatch = await f.Runtime.DispatchAsync(
            new(prepared, new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        dispatch.Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
        _ = (
            await f.Flows.SaveAsync(graph.Draft with { Id = saved.FlowId }, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
    }

    [Test]
    public async Task CommandArguments_RealScenarioUsesOriginalTailAndReadOnlyDerivedPortsWithoutLiveWrites()
    {
        var writes = new ScenarioWriteGuard();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var graph = TypedCommandTimer(f.HostId);
        _ = f
            .Expressions.Evaluate(
                new(AutomationExpressionLanguage.CurrentVersion, "seconds"),
                TypedCommandContext(f.HostId, "1 tea")
            )
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
        _ = (
            await f.Flows.SaveAsync(graph.Draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var scenario = f.Scenarios.CreateDefaultFixture(graph.Draft, graph.Source.Id) with
        {
            CommandArgumentTail = "\t1.001 \"cup  of\"\ttea  ",
        };
        var roundtrip = AutomationScenarioSerialization.Deserialize(
            AutomationScenarioSerialization.Serialize(scenario)
        );
        roundtrip.CommandArgumentTail.ShouldBe(scenario.CommandArgumentTail);
        writes.Armed = true;
        var result = (
            await f.Scenarios.RunAsync(graph.Draft, roundtrip, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result.Nodes.ShouldContain(node => node.NodeId == graph.Delay.Id);
        var trace = await ReadTraceAsync(f, result.TraceId);
        var due = trace
            .Events.Single(item => item.Event.Kind == AutomationTraceEventKind.Delay)
            .Event.DueAtUtc;
        due.ShouldBe(scenario.ClockUtc.AddMilliseconds(1001));
        JsonSerializer.Serialize(trace).ShouldNotContain("cup  of");
        var invalid = (
            await f.Scenarios.RunAsync(
                graph.Draft,
                scenario with
                {
                    CommandArgumentTail = "private-invalid tea",
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        invalid.Errors.ShouldContain(error => error.Code == "scenario-command-arguments-invalid");
        JsonSerializer.Serialize(invalid).ShouldNotContain("private-invalid");
        writes.Writes.ShouldBe(0);
        f.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task CommandArguments_ExpandedFrozenInvocationKeepsTypedInputAcrossSchemaAndLiveGraphEdits()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var ports = new AutomationSubflowInterface(
            [
                SubflowPort("seconds", AutomationPortValueType.Number),
                SubflowPort("name", AutomationPortValueType.Text),
            ],
            []
        );
        var child = Subflow(f.HostId, ports);
        var timer = TypedCommandTimer(f.HostId);
        var transform = timer.Draft.Nodes.Single(node => node.Definition.TypeId == "cel-transform");
        var start = timer.Draft.Nodes.Single(node =>
            node.Definition.TypeId == "send-chat" && node.Id != timer.Finish.Id
        );
        var entry = child.Graph.Nodes[0];
        var exit = child.Graph.Nodes[1];
        child = child with
        {
            Graph = child.Graph with
            {
                Nodes = [entry, transform, start, timer.Delay, timer.Finish, exit],
                Edges =
                [
                    Edge(entry, "complete", start),
                    Edge(start, "complete", timer.Delay),
                    Edge(timer.Delay, "complete", timer.Finish),
                    Edge(timer.Finish, "complete", exit),
                    Edge(entry, "seconds", transform, "seconds", AutomationEdgeKind.Data),
                    Edge(
                        transform,
                        "milliseconds",
                        timer.Delay,
                        "duration",
                        AutomationEdgeKind.Data
                    ),
                    Edge(entry, "name", start, "message", AutomationEdgeKind.Data),
                    Edge(entry, "name", timer.Finish, "message", AutomationEdgeKind.Data),
                ],
            },
        };
        var revision = await Publish(Subflows(f), child);
        var invoke = Invoke(revision) with
        {
            InputBindings = Bindings("seconds", AutomationInputBindingMode.Connected)
                .Add(new("name"), new(AutomationInputBindingMode.Connected, null)),
        };
        var caller = Draft(
            f.HostId,
            [timer.Source, invoke],
            [
                Edge(timer.Source, "flow", invoke),
                Edge(timer.Source, "argument.seconds", invoke, "seconds", AutomationEdgeKind.Data),
                Edge(timer.Source, "argument.name", invoke, "name", AutomationEdgeKind.Data),
            ]
        );
        var saved = (
            await f.Flows.SaveAsync(caller, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var run = (
            await f.Runtime.DispatchAsync(
                new(
                    TypedCommandContext(f.HostId, "1 frozen child"),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        f.Chat.Messages.ShouldBe(["frozen child"]);
        _ = (
            await f.Flows.SaveAsync(
                Draft(f.HostId, [timer.Source], []) with
                {
                    Id = saved.FlowId,
                },
                default
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        (await SaveArgumentSchema(f, null)).ShouldBeNull();
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = f.NewRuntime();
        await resumed.ResumeDueAsync(default);
        await resumed.ResumeDueAsync(default);
        (await resumed.ResumeAsync(run, default)).Status.ShouldBe(AutomationResumeStatus.Completed);
        f.Chat.Messages.ShouldBe(["frozen child", "frozen child"]);
        var trace = await ReadTraceAsync(f, new(run.Value));
        trace.Events.ShouldContain(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit);
        JsonSerializer.Serialize(trace).ShouldNotContain("frozen child");
    }

    private static AutomationContext TypedCommandContext(int hostId, string tail) =>
        Context(hostId) with
        {
            CommandArguments = CustomCommandArguments
                .Parse(7, "timer", CustomCommandArgumentTests.TimerSchema, tail)
                .ShouldBeOfType<CustomCommandArgumentParse.Prepared>()
                .Arguments,
            Arguments =
            [
                .. tail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select((word, index) => new AutomationArgument(index, word)),
            ],
        };

    private static (
        AutomationFlowDraft Draft,
        AutomationFlowDraftNode Source,
        AutomationFlowDraftNode Delay,
        AutomationFlowDraftNode Finish
    ) TypedCommandTimer(int hostId)
    {
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var transform = Node(
            "cel-transform",
            TransformJson(
                [
                    new(
                        "seconds",
                        "seconds",
                        "Seconds",
                        "seconds-input",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        0m
                    ),
                ],
                [
                    new(
                        "milliseconds",
                        "Milliseconds",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        "seconds * 1000"
                    ),
                ]
            ),
            bindings: Bindings("seconds-input", AutomationInputBindingMode.Connected)
        );
        var start = Node(
            "send-chat",
            """{"message":""}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var delay = Node(
            "delay",
            """{"duration-milliseconds":1}""",
            bindings: Bindings("duration-value-milliseconds", AutomationInputBindingMode.Connected)
        );
        var finish = Node(
            "send-chat",
            """{"message":""}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        return (
            Draft(
                hostId,
                [source, transform, start, delay, finish],
                [
                    Edge(source, "flow", start),
                    Edge(start, "complete", delay),
                    Edge(delay, "complete", finish),
                    Edge(source, "argument.seconds", transform, "seconds", AutomationEdgeKind.Data),
                    Edge(transform, "milliseconds", delay, "duration", AutomationEdgeKind.Data),
                    Edge(source, "argument.name", start, "message", AutomationEdgeKind.Data),
                    Edge(source, "argument.name", finish, "message", AutomationEdgeKind.Data),
                ]
            ),
            source,
            delay,
            finish
        );
    }

    private static async Task<CustomCommandConfigurationSaveFailure?> SaveArgumentSchema(
        RuntimeFixture f,
        CustomCommandArgumentSchema? schema
    )
    {
        await using var db = await f.Database.CreateDbContextAsync();
        var commands = await db
            .CustomCommands.Include(command => command.Action)
            .Include(command => command.Aliases)
            .Include(command => command.AllowedUsers)
            .Where(command => command.HostId == f.HostId)
            .ToListAsync();
        var editors = commands.Select(CustomCommandConfigurationMapper.ToEditor).ToList();
        foreach (var editor in editors)
        {
            editor.Aliases = editor.Name;
        }
        editors.Single(command => command.Id == 7).ArgumentSchema = schema;
        var draft = new CustomCommandConfiguration { Commands = editors };
        var validated = CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );
        return await new CustomCommandConfigurationGraphWriter(
            f.Database,
            new NoOverlayCues(),
            f.Clock
        ).WriteAsync(f.HostId, validated, CancellationToken.None);
    }
}
