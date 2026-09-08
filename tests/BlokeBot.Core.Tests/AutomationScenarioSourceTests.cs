using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task Scenario_SourceContextRoundTripsWithoutShadowingExpressionOrSourceValues()
    {
        const string Viewer = "Fixture viewer";
        var writes = new ScenarioWriteGuard();
        var projector = new TestPureHandler(
            DisplayNameHandler().Contract,
            input =>
            {
                var resolved = input.Inputs[new("actor")];
                resolved
                    .Value.ShouldBeOfType<AutomationValue.Actor>()
                    .Value.DisplayName.ShouldBe(Viewer);
                resolved.Provenance.ShouldBe([
                    AutomationValueProvenance.PublicDisplayName,
                    AutomationValueProvenance.PublicLogin,
                ]);
                return new AutomationPureNodeResult.Succeeded(
                    ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                        new("value"),
                        new(
                            new AutomationValue.Text(Viewer),
                            [
                                .. resolved
                                    .Provenance.Append(AutomationValueProvenance.Generated)
                                    .Order(),
                            ]
                        )
                    )
                );
            }
        );
        await using var fixture = await RuntimeFixture.CreateAsync(
            handlers: [projector],
            databaseInterceptors: [writes]
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var projection = Node(
            "test-display-name-transform",
            "{}",
            bindings: Bindings("actor", AutomationInputBindingMode.Connected)
        );
        var connected = Node(
            "send-chat",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var expression = Node(
            "send-chat",
            """{"message":"fallback"}""",
            bindings: Bindings(
                "message",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "actor.display_name")
            )
        );
        var draft = Draft(
            fixture.HostId,
            [source, projection, connected, expression],
            [
                Edge(source, "flow", connected),
                Edge(connected, "complete", expression),
                Edge(source, "actor", projection, "actor", AutomationEdgeKind.Data),
                Edge(projection, "value", connected, "message", AutomationEdgeKind.Data),
            ]
        );
        var flowId = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        var defaults = fixture.Scenarios.CreateDefaultFixture(draft, source.Id);
        var input = defaults with
        {
            Context = defaults.Context with
            {
                Actor = defaults.Context.Actor! with { DisplayName = Viewer },
                Stream = null,
            },
        };
        var saved = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            flowId,
            null,
            "Canonical viewer",
            input,
            CancellationToken.None
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var reloaded = (
            await fixture.Scenarios.ListAsync(new(fixture.HostId), flowId, CancellationToken.None)
        ).ShouldHaveSingleItem();
        AutomationScenarioSerialization
            .Serialize(reloaded.Fixture)
            .ShouldBe(AutomationScenarioSerialization.Serialize(input));
        writes.Armed = true;
        var result = (
            await fixture.Scenarios.RunSavedAsync(
                new(fixture.HostId),
                flowId,
                saved.Id!.Value,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result.Nodes[^1].ResolvedInputs.ShouldHaveSingleItem().DisplayValue.ShouldBe(Viewer);
        projector.Calls.ShouldBe(1);
        var injected = new Dictionary<AutomationVariableName, AutomationVariable>
        {
            [new("viewer_count")] = new(
                new AutomationValue.Number(24),
                AutomationDataSensitivity.Safe
            ),
            [new("actor")] = new(
                new AutomationValue.Actor(new("shadow", "Shadow")),
                AutomationDataSensitivity.Safe
            ),
            [new("arguments")] = new(
                new AutomationValue.Arguments([]),
                AutomationDataSensitivity.Safe
            ),
            [new("channel")] = new(
                new AutomationValue.Channel(new("shadow", "Shadow")),
                AutomationDataSensitivity.Safe
            ),
            [new("stream")] = new(
                new AutomationValue.Null(AutomationPortValueType.Stream),
                AutomationDataSensitivity.Sensitive
            ),
            [new("event-time")] = new(
                new AutomationValue.Timestamp(input.ClockUtc),
                AutomationDataSensitivity.Sensitive
            ),
            [new("event")] = new(new AutomationValue.Map([]), AutomationDataSensitivity.Safe),
            [new("timestamps")] = new(new AutomationValue.Map([]), AutomationDataSensitivity.Safe),
        };
        foreach (var assignment in injected)
        {
            var shadowed = input with
            {
                Context = input.Context with { Variables = new([assignment]) },
            };
            var invalid = (
                await fixture.Scenarios.RunAsync(draft, shadowed, CancellationToken.None)
            ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
            invalid.Errors.ShouldContain(error => error.Code == "scenario-source-variable-invalid");
            (
                await fixture.Scenarios.SaveAsync(
                    new(fixture.HostId),
                    flowId,
                    saved.Id,
                    "Rejected shadow",
                    shadowed,
                    CancellationToken.None
                )
            ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Invalid);
        }
        projector.Calls.ShouldBe(1);
        var preserved = (
            await fixture.Scenarios.ListAsync(new(fixture.HostId), flowId, CancellationToken.None)
        ).ShouldHaveSingleItem();
        AutomationScenarioSerialization
            .Serialize(preserved.Fixture)
            .ShouldBe(AutomationScenarioSerialization.Serialize(input));
        writes.Writes.ShouldBe(0);
        fixture.Chat.Calls.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_RequiredCanonicalStreamRejectsAbsentDataWithoutDuplicatingTimestamps()
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var source = Node("stream-online", "{}");
        var draft = Draft(fixture.HostId, [source], []);
        var flowId = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        var input = fixture.Scenarios.CreateDefaultFixture(draft, source.Id);
        var saved = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            flowId,
            null,
            "Stream online",
            input,
            CancellationToken.None
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var reloaded = (
            await fixture.Scenarios.ListAsync(new(fixture.HostId), flowId, CancellationToken.None)
        ).ShouldHaveSingleItem();
        reloaded.Fixture.Context.Stream.ShouldBe(input.Context.Stream);
        reloaded.Fixture.Context.Timestamps.ShouldBe(input.Context.Timestamps);
        reloaded.Fixture.Context.Variables.ForExecution().ShouldBeEmpty();
        writes.Armed = true;
        _ = (
            await fixture.Scenarios.RunSavedAsync(
                new(fixture.HostId),
                flowId,
                saved.Id!.Value,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        var missing = input with { Context = input.Context with { Stream = null } };
        var invalid = (
            await fixture.Scenarios.RunAsync(draft, missing, CancellationToken.None)
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
        invalid.Errors.ShouldContain(error =>
            error.PortId == new AutomationPortId("stream")
            && error.Code == "scenario-source-value-invalid"
        );
        (
            await fixture.Scenarios.SaveAsync(
                new(fixture.HostId),
                flowId,
                saved.Id,
                "Missing stream",
                missing,
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Invalid);
        (await fixture.Scenarios.ListAsync(new(fixture.HostId), flowId, CancellationToken.None))
            .ShouldHaveSingleItem()
            .Fixture.Context.Stream.ShouldBe(input.Context.Stream);
        writes.Writes.ShouldBe(0);
    }

    [Test]
    public async Task Scenario_DeclaredVariablesEnforceRequiredNullableTypeAndSensitivity()
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var source = Node("test-number-source", "{}");
        var condition = Node(
            "condition",
            """{"predicate":false}""",
            bindings: Bindings(
                "predicate",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "value == 73")
            )
        );
        var draft = Draft(fixture.HostId, [source, condition], [Edge(source, "flow", condition)]);
        var flowId = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        var defaults = fixture.Scenarios.CreateDefaultFixture(draft, source.Id);
        _ = defaults
            .Context.Variables.ForExecution()
            .ShouldHaveSingleItem()
            .Value.Value.ShouldBeOfType<AutomationValue.Number>();
        var input = defaults with
        {
            Context = defaults.Context with
            {
                Variables = new(
                    new Dictionary<AutomationVariableName, AutomationVariable>
                    {
                        [new("value")] = new(
                            new AutomationValue.Number(73),
                            AutomationDataSensitivity.Safe
                        ),
                    }
                ),
            },
        };
        var saved = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            flowId,
            null,
            "Declared number",
            input,
            CancellationToken.None
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var nullableSource = Node("test-nullable-number-source", "{}");
        var nullableCondition = Node(
            "condition",
            """{"predicate":false}""",
            bindings: Bindings(
                "predicate",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "value == null")
            )
        );
        var nullableDraft = Draft(
            fixture.HostId,
            [nullableSource, nullableCondition],
            [Edge(nullableSource, "flow", nullableCondition)]
        );
        var nullableFlowId = await fixture.SaveAsync(nullableDraft.Nodes, nullableDraft.Edges);
        var nullableDefaults = fixture.Scenarios.CreateDefaultFixture(
            nullableDraft,
            nullableSource.Id
        );
        var nullableInput = nullableDefaults with
        {
            Context = nullableDefaults.Context with { Variables = new([]) },
        };
        var nullableSaved = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            nullableFlowId,
            null,
            "Nullable number",
            nullableInput,
            CancellationToken.None
        );
        nullableSaved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        writes.Armed = true;
        var result = (
            await fixture.Scenarios.RunSavedAsync(
                new(fixture.HostId),
                flowId,
                saved.Id!.Value,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result.Nodes[^1].OutcomeCode.ShouldBe("condition-true");
        var nullableResult = (
            await fixture.Scenarios.RunSavedAsync(
                new(fixture.HostId),
                nullableFlowId,
                nullableSaved.Id!.Value,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        nullableResult.Nodes[^1].OutcomeCode.ShouldBe("condition-true");
        foreach (
            var variables in new AutomationVariableSet[]
            {
                new([]),
                new(
                    new Dictionary<AutomationVariableName, AutomationVariable>
                    {
                        [new("value")] = new(
                            new AutomationValue.Text("wrong type"),
                            AutomationDataSensitivity.Safe
                        ),
                    }
                ),
                new(
                    new Dictionary<AutomationVariableName, AutomationVariable>
                    {
                        [new("value")] = new(
                            new AutomationValue.Number(73),
                            AutomationDataSensitivity.Sensitive
                        ),
                    }
                ),
                new(
                    new Dictionary<AutomationVariableName, AutomationVariable>
                    {
                        [new("value")] = new(
                            new AutomationValue.Null(AutomationPortValueType.Number),
                            AutomationDataSensitivity.Safe
                        ),
                    }
                ),
            }
        )
        {
            var invalid = input with { Context = input.Context with { Variables = variables } };
            _ = (
                await fixture.Scenarios.RunAsync(draft, invalid, CancellationToken.None)
            ).ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>();
            (
                await fixture.Scenarios.SaveAsync(
                    new(fixture.HostId),
                    flowId,
                    saved.Id,
                    "Rejected value",
                    invalid,
                    CancellationToken.None
                )
            ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Invalid);
        }
        writes.Writes.ShouldBe(0);
    }
}
