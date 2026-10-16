using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments("tea")]
    [Arguments("one two three four five six seven eight nine ten")]
    [Arguments("\"cup  of\"\ttea")]
    public async Task Scenario_RawCommandUsesCanonicalGrammarAndCurrentTypedPortsInVirtualTime(
        string name
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var f = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var timer = TypedCommandTimer(f.HostId);
        var start = timer.Draft.Nodes.Single(node =>
            node.Definition.TypeId == "send-chat" && node.Id != timer.Finish.Id
        );
        var condition = Node("condition", """{"predicate":true}""");
        var draft = timer.Draft with
        {
            Nodes = [.. timer.Draft.Nodes, condition],
            Edges =
            [
                .. timer.Draft.Edges.Where(edge =>
                    edge.SourceNodeId != timer.Source.Id || edge.Kind != AutomationEdgeKind.Flow
                ),
                Edge(timer.Source, "flow", condition),
                Edge(condition, "yes", start),
            ],
        };
        var input = f.Scenarios.CreateDefaultFixture(draft, timer.Source.Id);
        var raw = "!AUTOMATION-SEVEN   60\t" + name + "  ";
        ChatCommandDispatcher.TryParseCommand(raw, out var route, out _).ShouldBeTrue();
        route.ShouldBe("AUTOMATION-SEVEN");
        writes.Armed = true;
        var wallStart = f.Clock.GetUtcNow();
        var result = (
            await f.Scenarios.RunAsync(draft, input, default, raw)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        f.Clock.GetUtcNow().ShouldBe(wallStart);
        var trace = await ReadTraceAsync(f, result.TraceId);
        var messages = result.Preview.Where(value => value.WouldSend is not null).ToArray();
        messages.Select(value => value.WouldSend).ShouldBe([name + "  ", name + "  "]);
        messages.Select(value => value.VirtualOffsetMilliseconds).ShouldBe([0L, 60_000L]);
        var converted = trace.Events.Single(entry =>
            entry.Event.Kind == AutomationTraceEventKind.Outputs
            && entry.Event.Node?.DefinitionId.Value == "cel-transform"
        );
        result
            .Preview.Single(value => value.Sequence == converted.Sequence)
            .Values.ShouldContain(value =>
                value.PortId.Value == "milliseconds"
                && value.ValueType == AutomationPortValueType.Number
                && value.DisplayValue == "60000"
            );
        trace.Events.ShouldContain(entry =>
            entry.Event.Kind == AutomationTraceEventKind.Branch
            && entry.Event.Node!.Id == condition.Id
            && entry.Event.SelectedPort!.Value.Value == "yes"
        );
        var delay = trace.Events.Single(entry =>
            entry.Event.Kind == AutomationTraceEventKind.Delay
        );
        delay.Event.DueAtUtc.ShouldBe(input.ClockUtc.AddMinutes(1));
        result
            .Preview.Single(value => value.Sequence == trace.Events[^1].Sequence)
            .VirtualOffsetMilliseconds.ShouldBe(60_000L);
        JsonSerializer.Serialize(trace).ShouldNotContain(name);
        AutomationScenarioSerialization.Serialize(input).ShouldNotContain(name);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationScenarios.CountAsync()).ShouldBe(0);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        writes.Writes.ShouldBe(0);
        f.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_RawIdentityIsHostBoundAndUsesCurrentSchemaWithoutMutatingTheFixture()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var timer = TypedCommandTimer(f.HostId);
        var input = f.Scenarios.CreateDefaultFixture(timer.Draft, timer.Source.Id);
        var baseline = AutomationScenarioSerialization.Serialize(input);
        foreach (
            var raw in new[]
            {
                "!automation-eight 60 tea",
                "!unknown 60 tea",
                " !automation-seven 60 tea",
                "automation-seven 60 tea",
                "! 60 tea",
                "!automation-seven\t60 tea",
                "!automation-seven 0 tea",
                "!automation-seven 60 " + string.Join(' ', Enumerable.Repeat("word", 11)),
            }
        )
        {
            _ = (
                await f.Scenarios.RunAsync(timer.Draft, input, default, raw)
            ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        }
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationTraces.CountAsync()).ShouldBe(0);
            var command = await db.CustomCommands.SingleAsync(command => command.Id == 7);
            command.ArgumentSchema = CustomCommandArgumentTests.TimerSchema with
            {
                Fields =
                [
                    new("seconds", CustomCommandArgumentKind.Number, 60),
                    CustomCommandArgumentTests.TimerSchema.Fields[1],
                ],
            };
            _ = await db.SaveChangesAsync();
        }
        _ = (
            await f.Scenarios.RunAsync(timer.Draft, input, default, "!automation-seven 60 tea")
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        _ = (
            await f.Scenarios.RunAsync(timer.Draft, input, default, "!automation-seven 61 tea")
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        AutomationScenarioSerialization.Serialize(input).ShouldBe(baseline);
        var flow = (await f.Flows.SaveAsync(timer.Draft, default))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var sample = new AutomationScenarioEditor(input);
        sample.SetRawCommand("!automation-seven 61 explicit-sample");
        var saved = await f.Scenarios.SaveAsync(
            new(f.HostId),
            flow,
            null,
            "Explicit inputs",
            sample.Fixture,
            default
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var reloaded = (
            await f.Scenarios.ListAsync(new(f.HostId), flow, default)
        ).ShouldHaveSingleItem();
        reloaded.Fixture.CommandArgumentTail.ShouldBe("61 explicit-sample");
        AutomationScenarioSerialization
            .Serialize(reloaded.Fixture)
            .ShouldNotContain("!automation-seven");
        var replay = (
            await f.Scenarios.RunSavedAsync(new(f.HostId), flow, saved.Id!.Value, default)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        replay
            .Preview.Where(value => value.WouldSend != null)
            .Select(value => value.WouldSend)
            .ShouldBe(["explicit-sample", "explicit-sample"]);
        f.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_CurrentDisplaysBoundSafePreparedValuesWithoutChangingDurableDiagnostics()
    {
        var secret = "preview-only-" + new string('x', 300);
        var handler = new TestPureHandler(
            DisplayNameHandler().Contract,
            input => new AutomationPureNodeResult.Succeeded(
                System.Collections.Immutable.ImmutableDictionary<
                    AutomationPortId,
                    AutomationResolvedValue
                >.Empty.Add(
                    new("value"),
                    new(
                        new AutomationValue.Text(secret),
                        [
                            .. input
                                .Inputs[new("actor")]
                                .Provenance.Append(AutomationValueProvenance.Generated)
                                .Distinct()
                                .Order(),
                        ],
                        ValueFreeDiagnostic: true
                    )
                )
            )
        );
        await using var f = await RuntimeFixture.CreateAsync(handlers: [handler]);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var producer = Node(
            "test-display-name-transform",
            "{}",
            bindings: Bindings("actor", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "send-chat",
            """{"message":""}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var draft = Draft(
            f.HostId,
            [source, producer, action],
            [
                Edge(source, "flow", action),
                Edge(source, "actor", producer, "actor", AutomationEdgeKind.Data),
                Edge(producer, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );
        var input = f.Scenarios.CreateDefaultFixture(draft, source.Id);
        var completed = (
            await f.Scenarios.RunAsync(draft, input, default)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var trace = await ReadTraceAsync(f, completed.TraceId);
        var output = trace.Events.Single(entry =>
            entry.Event.Kind == AutomationTraceEventKind.Outputs
            && entry.Event.Node!.Id == producer.Id
        );
        var display = completed
            .Preview.Single(value => value.Sequence == output.Sequence)
            .Values.Single()
            .DisplayValue;
        display.ShouldBe(secret[..200] + "...");
        completed.Preview.Single(value => value.WouldSend != null).WouldSend.ShouldBe(display);
        completed
            .Nodes.Single(node => node.NodeId == action.Id)
            .ResolvedInputs.Single()
            .DisplayValue.ShouldBe("available");
        JsonSerializer.Serialize(trace).ShouldNotContain("preview-only-");
        AutomationScenarioSerialization.Serialize(input).ShouldNotContain("preview-only-");
        f.Chat.Calls.ShouldBe(0);
    }
}
