using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false, AutomationInputBindingMode.Fixed, 1)]
    [Arguments(true, AutomationInputBindingMode.Fixed, 1)]
    [Arguments(true, AutomationInputBindingMode.Expression, 1)]
    [Arguments(true, AutomationInputBindingMode.Connected, 0)]
    [Arguments(true, AutomationInputBindingMode.Connected, 2)]
    public async Task DelayBinding_GraphAndFrozenResolverRejectInactiveOrMissingConnectionsBeforeFixtures(
        bool present,
        AutomationInputBindingMode mode,
        int incoming
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var number = Node("random-number", """{"minimum":23,"maximum":23}""");
        var delay = present ? BoundDelay(mode) : Node("delay", """{"duration-milliseconds":1}""");
        var data = Enumerable
            .Range(0, incoming)
            .Select(_ => Edge(number, "number", delay, "duration", AutomationEdgeKind.Data))
            .ToImmutableArray();
        var draft = Draft(
            fixture.HostId,
            [source, number, delay],
            [Edge(source, "flow", delay), .. data]
        );
        _ = (
            await fixture.Flows.ValidateDraftAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
        var consumer = FrozenDelay(delay);
        var frozen = new AutomationRuntimeSerialization.PersistedFlow(
            Guid.NewGuid(),
            fixture.HostId,
            AutomationFlowSchema.CurrentVersion,
            [consumer],
            [
                .. data.Select(edge => new AutomationRuntimeSerialization.PersistedEdge(
                    edge.Id,
                    edge.Kind,
                    edge.SourceNodeId.Value,
                    edge.SourcePortId.Value,
                    edge.TargetNodeId.Value,
                    edge.TargetPortId.Value
                )),
            ]
        );
        var result = await fixture.Catalog.Data.ResolveScenarioInputsAsync(
            new(fixture.HostId),
            Context(fixture.HostId),
            frozen,
            consumer,
            new AutomationScenarioCheckpointStore((_, _, _, _, _) => Task.CompletedTask),
            new CountingIntegerEntropy(0),
            [
                new(
                    delay.Id,
                    new("duration"),
                    new(new AutomationValue.Number(23), AutomationDataSensitivity.Safe)
                ),
            ],
            CancellationToken.None
        );
        result.ShouldBeOfType<AutomationInputResolution.Failed>().Code.ShouldBe("binding-invalid");
    }

    [Test]
    [Arguments(AutomationInputBindingMode.Fixed)]
    [Arguments(AutomationInputBindingMode.Expression)]
    [Arguments(AutomationInputBindingMode.Connected)]
    public async Task DelayBinding_FrozenLegacyIgnoresOldPayloadAndBoundFixedUsesExactCanonicalScalar(
        AutomationInputBindingMode oldMode
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var old = Node(
            "delay",
            """{"duration-milliseconds":2147483648}""",
            bindings: Bindings(
                "duration-milliseconds",
                oldMode,
                new(AutomationExpressionLanguage.CurrentVersion, "99999")
            )
        );
        var legacy = FrozenDelay(old);
        var frozen = new AutomationRuntimeSerialization.PersistedFlow(
            Guid.NewGuid(),
            fixture.HostId,
            AutomationFlowSchema.CurrentVersion,
            [legacy],
            []
        );
        var checkpoint = new AutomationScenarioCheckpointStore(
            (_, _, _, _, _) => Task.CompletedTask
        );
        var resolved = (
            await fixture.Catalog.Data.ResolveInputsAsync(
                new(fixture.HostId),
                Context(fixture.HostId),
                frozen,
                legacy,
                checkpoint,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationInputResolution.Available>();
        resolved.FieldValues.ContainsKey(AutomationDelayDurationBinding.ValueField).ShouldBeFalse();
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        AutomationNodeEvaluation
            .Delay(
                new(TimeSpan.FromTicks(2147483648L * TimeSpan.TicksPerMillisecond)),
                resolved.FieldValues,
                now
            )
            .ShouldBeOfType<AutomationNodeExecution.Succeeded>()
            .NextAvailableAtUtc.ShouldBe(now.AddTicks(2147483648L * TimeSpan.TicksPerMillisecond));
        var bound = legacy with
        {
            InputBindingsJson = AutomationRuntimeSerialization.SerializeInputBindings(
                old.InputBindings.Add(
                    AutomationDelayDurationBinding.ValueField,
                    new(AutomationInputBindingMode.Fixed, null)
                )
            ),
        };
        var active = (
            await fixture.Catalog.Data.ResolveInputsAsync(
                new(fixture.HostId),
                Context(fixture.HostId),
                frozen with
                {
                    Nodes = [bound],
                },
                bound,
                checkpoint,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationInputResolution.Available>();
        active
            .FieldValues[AutomationDelayDurationBinding.ValueField]
            .Value.ShouldBe(new AutomationValue.Number(2147483648m));
    }

    [Test]
    public void DelayBinding_TickBoundsRemainDistinctFromCalendarOverflowAndNeverCoerce()
    {
        var now = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        var literal = new DelayControlConfiguration(TimeSpan.FromMilliseconds(1));
        AutomationNodeExecution Evaluate(AutomationValue value) =>
            AutomationNodeEvaluation.Delay(
                literal,
                ImmutableDictionary<
                    AutomationConfigurationFieldId,
                    AutomationResolvedValue
                >.Empty.Add(
                    AutomationDelayDurationBinding.ValueField,
                    new(value, [AutomationValueProvenance.Generated])
                ),
                now
            );
        Evaluate(new AutomationValue.Number(AutomationDelayDurationBinding.MaximumMilliseconds))
            .ShouldBeOfType<AutomationNodeExecution.Failed>()
            .Code.ShouldBe("delay-unrepresentable");
        Evaluate(
                new AutomationValue.Number(AutomationDelayDurationBinding.MaximumMilliseconds + 1m)
            )
            .ShouldBeOfType<AutomationNodeExecution.Failed>()
            .Code.ShouldBe("delay-invalid-duration");
        Evaluate(new AutomationValue.Number(decimal.MaxValue))
            .ShouldBeOfType<AutomationNodeExecution.Failed>()
            .Code.ShouldBe("delay-invalid-duration");
        Evaluate(new AutomationValue.Text("17"))
            .ShouldBeOfType<AutomationNodeExecution.Failed>()
            .Code.ShouldBe("delay-invalid-duration");
        Evaluate(new AutomationValue.Number(2147483648m))
            .ShouldBeOfType<AutomationNodeExecution.Succeeded>()
            .NextAvailableAtUtc.ShouldBe(now.AddTicks(2147483648L * TimeSpan.TicksPerMillisecond));
        AutomationNodeEvaluation
            .Delay(
                literal,
                ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue>.Empty,
                DateTime.MaxValue
            )
            .ShouldBeOfType<AutomationNodeExecution.Failed>()
            .Code.ShouldBe("delay-unrepresentable");
    }

    [Test]
    public async Task DelayBinding_CancelledResumeLeavesContinuationForFreshRuntime()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = BoundDelay(AutomationInputBindingMode.Expression, "int(arguments[0])");
        var action = Node("send-chat", """{"message":"once after cancellation"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var run = (
            await fixture.Runtime.DispatchAsync(
                new(Context(fixture.HostId, "17"), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(17));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.NewRuntime().ResumeAsync(run, cancellation.Token)
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        (await fixture.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        fixture.Chat.Messages.ShouldBe(["once after cancellation"]);
    }

    [Test]
    [Arguments(AutomationInputBindingMode.Fixed)]
    [Arguments(AutomationInputBindingMode.Connected)]
    public async Task DelayBinding_BoundInactiveExpressionSurvivesSaveDuplicateAndFrozenResolution(
        AutomationInputBindingMode mode
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var number = Node("random-number", """{"minimum":23,"maximum":23}""");
        var delay = BoundDelay(AutomationInputBindingMode.Expression, "99999") with
        {
            InputBindings = Bindings(
                "duration-value-milliseconds",
                mode,
                new(AutomationExpressionLanguage.CurrentVersion, "99999")
            ),
        };
        var action = Node("send-chat", """{"message":"bound inactive expression"}""");
        var edges = ImmutableArray.Create(
            Edge(source, "flow", delay),
            Edge(delay, "complete", action)
        );
        if (mode == AutomationInputBindingMode.Connected)
        {
            edges = edges.Add(Edge(number, "number", delay, "duration", AutomationEdgeKind.Data));
        }
        var flow = await fixture.SaveAsync(
            mode == AutomationInputBindingMode.Connected
                ? [source, number, delay, action]
                : [source, delay, action],
            edges
        );
        var duplicate = (
            await fixture.Flows.DuplicateAsync(new(fixture.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDuplicateOutcome.Duplicated>();
        var loaded = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(snapshot => snapshot.Draft.Id == duplicate.FlowId)
            .Draft;
        loaded
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
        ).AvailableAtUtc.ShouldBe(
            started.AddMilliseconds(mode == AutomationInputBindingMode.Connected ? 23 : 1)
        );
    }

    private static AutomationRuntimeSerialization.PersistedNode FrozenDelay(
        AutomationFlowDraftNode node
    ) =>
        new(
            node.Id.Value,
            node.Definition.TypeId,
            node.Definition.SchemaVersion,
            node.Definition.Configuration.GetRawText(),
            AutomationRuntimeSerialization.SerializeInputBindings(node.InputBindings),
            node.ExpressionLanguageVersion.Value,
            node.FailurePolicy == AutomationNodeFailurePolicy.Continue
        );
}
