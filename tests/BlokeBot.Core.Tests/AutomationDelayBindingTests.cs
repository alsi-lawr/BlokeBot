using System.Collections.Immutable;
using System.Globalization;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DelayBinding_ProductionArgumentsAndConnectedNumberPersistExactDue(
        bool connected
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var number = Node("random-number", """{"minimum":1001,"maximum":1001}""");
        var delay = BoundDelay(
            connected
                ? AutomationInputBindingMode.Connected
                : AutomationInputBindingMode.Expression,
            "int(arguments[0])"
        );
        var action = Node("send-chat", """{"message":"after exact due"}""");
        var edges = ImmutableArray.Create(
            Edge(source, "flow", delay),
            Edge(delay, "complete", action)
        );
        if (connected)
        {
            edges = edges.Add(Edge(number, "number", delay, "duration", AutomationEdgeKind.Data));
        }
        var draft = Draft(
            fixture.HostId,
            connected ? [source, number, delay, action] : [source, delay, action],
            edges
        );
        var flowId = (await fixture.Flows.SaveAsync(draft, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var started = fixture.Clock.GetUtcNow().UtcDateTime;
        var context = Context(fixture.HostId, "1001");
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(context, new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Chat.Messages.ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var continuation = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == action.Id.Value
            );
            continuation.AvailableAtUtc.ShouldBe(
                started.AddTicks(1001 * TimeSpan.TicksPerMillisecond)
            );
            (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                AutomationFlowRunStatus.Waiting
            );
        }
        var changed = BoundDelay(
            AutomationInputBindingMode.Expression,
            "int(arguments[0]) + 5000"
        ) with
        {
            Id = delay.Id,
        };
        _ = (
            await fixture.Flows.SaveAsync(
                draft with
                {
                    Id = flowId,
                    Nodes = draft.Nodes.Replace(delay, changed),
                    Edges = edges
                        .Where(edge => edge.Kind != AutomationEdgeKind.Data)
                        .ToImmutableArray(),
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        fixture.Clock.Advance(TimeSpan.FromTicks((1001 * TimeSpan.TicksPerMillisecond) - 1));
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["after exact due"]);

        var writes = new ScenarioWriteGuard();
        await using var isolated = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        writes.Armed = true;
        var scenarioDraft = draft with { HostId = new(isolated.HostId) };
        var scenario = isolated.Scenarios.CreateDefaultFixture(scenarioDraft, source.Id);
        scenario = scenario with
        {
            Context = scenario.Context with { Arguments = [new(0, "1001")] },
        };
        var result = (
            await isolated.Scenarios.RunAsync(scenarioDraft, scenario, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result
            .Nodes[^1]
            .VirtualTimeUtc.ShouldBe(
                scenario.ClockUtc.AddTicks(1001 * TimeSpan.TicksPerMillisecond)
            );
        result
            .Nodes.Single(node => node.NodeId == delay.Id)
            .ResolvedInputs.ShouldContain(input =>
                input.PortId == AutomationDelayDurationBinding.Port
                && input.ValueType == AutomationPortValueType.Number
            );
        writes.Writes.ShouldBe(0);
        isolated.Chat.Calls.ShouldBe(0);
    }

    [Test]
    [Arguments(AutomationInputBindingMode.Fixed)]
    [Arguments(AutomationInputBindingMode.Expression)]
    [Arguments(AutomationInputBindingMode.Connected)]
    public async Task DelayBinding_LegacyIgnoredPayloadSurvivesEditorSaveDuplicateAndFrozenExecution(
        AutomationInputBindingMode oldMode
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node(
            "delay",
            """{"duration-milliseconds":17}""",
            bindings: Bindings(
                "duration-milliseconds",
                oldMode,
                new(AutomationExpressionLanguage.CurrentVersion, "99999")
            )
        );
        var action = Node("send-chat", """{"message":"legacy literal"}""");
        var draft = Draft(
            fixture.HostId,
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Single(value =>
                value.Descriptor.Id == AutomationDefinitionIds.DelayControl
            )
            .Descriptor;
        var restored = AutomationEditorNode.Restore(delay, definition);
        restored
            .Binding(AutomationDelayDurationBinding.ValueField)
            .Mode.ShouldBe(AutomationInputBindingMode.Fixed);
        restored.Draft().InputBindings.ShouldBe(delay.InputBindings);
        restored.SetValue(AutomationDelayDurationBinding.ValueField, "19");
        var edited = restored.Draft();
        edited.InputBindings.ContainsKey(AutomationDelayDurationBinding.ValueField).ShouldBeFalse();
        edited
            .Definition.Configuration.GetProperty("duration-milliseconds")
            .GetInt64()
            .ShouldBe(19);
        edited
            .Definition.Configuration.TryGetProperty("duration-value-milliseconds", out _)
            .ShouldBeFalse();
        var flowId = (
            await fixture.Flows.SaveAsync(
                draft with
                {
                    Nodes = draft.Nodes.Replace(delay, edited),
                },
                CancellationToken.None
            )
        )
            .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
            .FlowId;
        var duplicated = (
            await fixture.Flows.DuplicateAsync(new(fixture.HostId), flowId, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDuplicateOutcome.Duplicated>();
        var duplicate = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(flow => flow.Draft.Id == duplicated.FlowId)
            .Draft;
        duplicate
            .Nodes.Single(node => node.Definition.TypeId == "delay")
            .InputBindings.ShouldBe(delay.InputBindings);
        var started = fixture.Clock.GetUtcNow().UtcDateTime;
        _ = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        (
            await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == action.Id.Value)
        ).AvailableAtUtc.ShouldBe(started.AddMilliseconds(19));
        var frozen = AutomationRuntimeSerialization
            .RestoreDefinition((await db.AutomationFlowRuns.SingleAsync()).DefinitionJson)
            .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>()
            .Flow;
        AutomationRuntimeSerialization
            .RestoreInputBindings(
                frozen.Nodes.Single(node => node.Id == delay.Id.Value).InputBindingsJson
            )
            .ShouldBeOfType<AutomationInputBindingsRestoreOutcome.Available>()
            .Bindings.ShouldBe(delay.InputBindings);
    }

    [Test]
    [Arguments("0", AutomationNodeFailurePolicy.Stop)]
    [Arguments("-1", AutomationNodeFailurePolicy.Continue)]
    [Arguments("1.5", AutomationNodeFailurePolicy.Stop)]
    [Arguments("922337203685478", AutomationNodeFailurePolicy.Continue)]
    [Arguments("0", AutomationNodeFailurePolicy.Continue)]
    [Arguments("-1", AutomationNodeFailurePolicy.Stop)]
    [Arguments("1.5", AutomationNodeFailurePolicy.Continue)]
    [Arguments("922337203685478", AutomationNodeFailurePolicy.Stop)]
    public async Task DelayBinding_InvalidNumericUsesStopContinueWithoutWait(
        string number,
        AutomationNodeFailurePolicy policy
    )
    {
        var value = new AutomationValue.Number(decimal.Parse(number, CultureInfo.InvariantCulture));
        var producerHandler = CompositeValueHandler(
            "test-number-value",
            AutomationPortValueType.Number,
            value
        );
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [producerHandler]);
        var producer = Node("test-number-value", "{}");
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var before = Node("send-chat", """{"message":"already completed"}""");
        var delay = BoundDelay(AutomationInputBindingMode.Connected) with
        {
            FailurePolicy = policy,
        };
        var after = Node("send-chat", """{"message":"continued immediately"}""");
        var draft = Draft(
            fixture.HostId,
            [source, producer, before, delay, after],
            [
                Edge(source, "flow", before),
                Edge(before, "complete", delay),
                Edge(delay, "complete", after),
                Edge(producer, "value", delay, "duration", AutomationEdgeKind.Data),
            ]
        );
        _ = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        _ = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBe(
            policy == AutomationNodeFailurePolicy.Stop
                ? ["already completed"]
                : ["already completed", "continued immediately"]
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.Include(value => value.NodeRuns).SingleAsync();
        run.Status.ShouldBe(
            policy == AutomationNodeFailurePolicy.Stop
                ? AutomationFlowRunStatus.Failed
                : AutomationFlowRunStatus.Completed
        );
        var failure = run.NodeRuns.Single(node => node.NodeId == delay.Id.Value);
        failure.OutcomeCode.ShouldBe("delay-invalid-duration");
        run.NodeRuns.ShouldNotContain(node => node.Status == AutomationNodeRunStatus.Pending);
        producerHandler.Calls.ShouldBe(1);
        var scenario = fixture.Scenarios.CreateDefaultFixture(draft, source.Id) with
        {
            ConnectedInputs =
            [
                new(
                    delay.Id,
                    AutomationDelayDurationBinding.Port,
                    new(value, AutomationDataSensitivity.Safe)
                ),
            ],
        };
        var simulated = await fixture.Scenarios.RunAsync(draft, scenario, CancellationToken.None);
        var nodes =
            policy == AutomationNodeFailurePolicy.Stop
                ? simulated.ShouldBeOfType<AutomationScenarioRunOutcome.Failed>().Nodes
                : simulated.ShouldBeOfType<AutomationScenarioRunOutcome.Completed>().Nodes;
        nodes
            .Single(node => node.NodeId == delay.Id)
            .OutcomeCode.ShouldBe("delay-invalid-duration");
        nodes[^1].VirtualTimeUtc.ShouldBe(scenario.ClockUtc);
    }

    [Test]
    [Arguments("arguments[0]", "not numeric")]
    [Arguments("int(arguments[0])", "not numeric")]
    [Arguments("int(arguments[4])", "3")]
    [Arguments("private_value", "3")]
    public async Task DelayBinding_UnavailableExpressionNeverFallsBackToLiteral(
        string expression,
        string argument
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = BoundDelay(AutomationInputBindingMode.Expression, expression);
        _ = await fixture.SaveAsync([source, delay], [Edge(source, "flow", delay)]);
        _ = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId, argument), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        var failed = await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == delay.Id.Value);
        failed.Status.ShouldBe(AutomationNodeRunStatus.Failed);
        failed.OutcomeCode.ShouldBe("input-resolution-failed");
        (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(AutomationFlowRunStatus.Failed);
    }

    private static AutomationFlowDraftNode BoundDelay(
        AutomationInputBindingMode mode,
        string expression = "17"
    ) =>
        Node(
            "delay",
            """{"duration-milliseconds":1}""",
            bindings: Bindings(
                "duration-value-milliseconds",
                mode,
                mode == AutomationInputBindingMode.Expression
                    ? new(AutomationExpressionLanguage.CurrentVersion, expression)
                    : null
            )
        );
}
