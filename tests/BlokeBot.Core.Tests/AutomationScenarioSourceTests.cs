using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments("""{"minimum-bits":100}""")]
    [Arguments("""{"minimum-bits":100,"maximum-bits":200}""")]
    public async Task Scenario_CheerSavedFixtureRunsBoundedAndUncappedSourcesWithoutRealEffects(
        string json
    )
    {
        var writes = new ScenarioWriteGuard();
        await using var fixture = await RuntimeFixture.CreateAsync(databaseInterceptors: [writes]);
        var source = Node("cheer", json);
        var action = Node("send-chat", """{"message":"Scenario cheer"}""");
        var draft = Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]);
        var flowId = await fixture.SaveAsync(draft.Nodes, draft.Edges);
        var input = fixture.Scenarios.CreateDefaultFixture(draft, source.Id);
        var saved = await fixture.Scenarios.SaveAsync(
            new(fixture.HostId),
            flowId,
            null,
            "Cheer scenario",
            input,
            CancellationToken.None
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        writes.Armed = true;
        var result = (
            await fixture.Scenarios.RunSavedAsync(
                new(fixture.HostId),
                flowId,
                saved.Id!.Value,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationScenarioRunOutcome.Completed>();
        result.Nodes.ShouldContain(node =>
            node.NodeId == action.Id && node.State == AutomationNodeRunState.Succeeded
        );
        writes.Writes.ShouldBe(0);
        fixture.Chat.Calls.ShouldBe(0);
    }

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
            "condition",
            """{"predicate":false}""",
            bindings: Bindings(
                "predicate",
                AutomationInputBindingMode.Expression,
                new(
                    AutomationExpressionLanguage.CurrentVersion,
                    "actor.display_name == 'Fixture viewer'"
                )
            )
        );
        var draft = Draft(
            fixture.HostId,
            [source, projection, connected, expression],
            [
                Edge(source, "flow", expression),
                Edge(expression, "yes", connected),
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
        result
            .Nodes.Single(node => node.NodeId == expression.Id)
            .OutcomeCode.ShouldBe("condition-true");
        result
            .Nodes.Single(node => node.NodeId == connected.Id)
            .State.ShouldBe(AutomationNodeRunState.Succeeded);
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
        var input = fixture.Scenarios.CreatePortableFixture(draft, source.Id);
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
        fixture.Scenarios.IsPortableFixture(draft, reloaded.Fixture).ShouldBeTrue();
        var manual = new AutomationScenarioEditor(reloaded.Fixture);
        manual.SetSourceValue(
            fixture
                .Scenarios.SourceFields(draft, source.Id)
                .Single(field => field.Id.Value == "stream_type"),
            new AutomationValue.Text("authored")
        );
        fixture.Scenarios.IsPortableFixture(draft, manual.Fixture).ShouldBeFalse();
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

    [Test]
    public async Task Scenario_RedemptionInputsReachPreparationAndLegacyAbsenceStaysHonest()
    {
        var writes = new ScenarioWriteGuard();
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.RewardsAndRedemptions,
            databaseInterceptors: [writes]
        );
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            _ = db.TwitchCustomRewards.Add(
                new()
                {
                    HostId = f.HostId,
                    ProviderRewardId = "authored-reward",
                    Title = "Hydrate",
                    Cost = 250,
                    IsManageable = true,
                    IsEnabled = true,
                    UpdatedAtUtc = f.Clock.GetUtcNow().UtcDateTime,
                }
            );
            _ = db.HostBroadcasterAuthorizations.Add(
                new()
                {
                    HostId = f.HostId,
                    TwitchUserId = "streamer-id",
                    ProtectedTokenPayload = [1],
                    AuthorizedScopes = ScopeSet.Format(
                        HostBroadcasterAuthorizationService.MilestoneScopes
                    ),
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var source = Node("reward-redemption", """{"completion-policy":"manual"}""");
        var action = Node("fulfil-redemption", "{}");
        var draft = Draft(f.HostId, [source, action], [Edge(source, "flow", action)]);
        var flow = await f.SaveAsync(draft.Nodes, draft.Edges);
        var defaults = f.Scenarios.CreateDefaultFixture(draft, source.Id);
        var legacy = defaults with { Context = defaults.Context with { Variables = new([]) } };
        var saved = await f.Scenarios.SaveAsync(
            new(f.HostId),
            flow,
            null,
            "Old redemption inputs",
            legacy,
            default
        );
        saved.Status.ShouldBe(AutomationScenarioAuthoringStatus.Saved);
        var reloaded = (
            await f.Scenarios.ListAsync(new(f.HostId), flow, default)
        ).ShouldHaveSingleItem();
        reloaded.Fixture.Context.Variables.ForExecution().ShouldBeEmpty();
        writes.Armed = true;
        (await f.Scenarios.RunSavedAsync(new(f.HostId), flow, saved.Id!.Value, default))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
            .Nodes[^1]
            .OutcomeCode.ShouldBe("redemption-context-missing");
        var editor = new AutomationScenarioEditor(reloaded.Fixture);
        var fields = f.Scenarios.SourceFields(draft, source.Id);
        editor.SetSourceValue(
            fields.Single(field => field.Id.Value == "redemption_id"),
            new AutomationValue.Text("authored-redemption")
        );
        editor.SetSourceValue(
            fields.Single(field => field.Id.Value == "reward_id"),
            new AutomationValue.Text("authored-reward")
        );
        editor.SetSourceValue(
            fields.Single(field => field.Id.Value == "user_input"),
            new AutomationValue.Text(new string('x', 500))
        );
        var prepared = AutomationNativeActionPreparation
            .Redemption(editor.Fixture.Context)
            .ShouldBeOfType<AutomationNativePreparation<AutomationPreparedRedemption>.Prepared>();
        prepared.Payload.ShouldBe(new("authored-redemption", "authored-reward"));
        (await f.Scenarios.RunAsync(draft, editor.Fixture, default))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Completed>()
            .Nodes[^1]
            .OutcomeCode.ShouldBe("action-simulated");
        var baseline = AutomationScenarioSerialization.Serialize(editor.Fixture);
        foreach (
            var (name, variable, code) in new (string, AutomationVariable, string)[]
            {
                (
                    "bits",
                    new(new AutomationValue.Number(4), AutomationDataSensitivity.Safe),
                    "scenario-source-variable-invalid"
                ),
                (
                    "reward_cost",
                    new(new AutomationValue.Text("not a number"), AutomationDataSensitivity.Safe),
                    "scenario-source-value-invalid"
                ),
                (
                    "user_input",
                    new(
                        new AutomationValue.Text("wrong sensitivity"),
                        AutomationDataSensitivity.Safe
                    ),
                    "scenario-source-value-invalid"
                ),
                (
                    "reward_title",
                    new(
                        new AutomationValue.Text(new string('x', 501)),
                        AutomationDataSensitivity.Safe
                    ),
                    "scenario-source-value-too-long"
                ),
                (
                    "reward_id",
                    new(
                        new AutomationValue.Null(AutomationPortValueType.Text),
                        AutomationDataSensitivity.Safe
                    ),
                    "scenario-source-value-invalid"
                ),
            }
        )
        {
            var invalid = editor.Fixture with
            {
                Context = editor.Fixture.Context with
                {
                    Variables = new(
                        editor
                            .Fixture.Context.Variables.ForExecution()
                            .ToImmutableDictionary()
                            .SetItem(new(name), variable)
                    ),
                },
            };
            (await f.Scenarios.RunAsync(draft, invalid, default))
                .ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>()
                .Errors.ShouldContain(error =>
                    error.Code == code && error.PortId == new AutomationPortId(name)
                );
            (
                await f.Scenarios.SaveAsync(
                    new(f.HostId),
                    flow,
                    saved.Id,
                    "Rejected inputs",
                    invalid,
                    default
                )
            ).Status.ShouldBe(AutomationScenarioAuthoringStatus.Invalid);
        }
        writes.Armed = false;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.HostBroadcasterAuthorizations.SingleAsync()).AuthorizedScopes = string.Empty;
            _ = await db.SaveChangesAsync();
        }
        writes.Armed = true;
        (await f.Scenarios.RunAsync(draft, editor.Fixture, default))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Failed>()
            .Nodes[^1]
            .OutcomeCode.ShouldBe("broadcaster-authorization-missing");
        writes.Armed = false;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.Hosts.SingleAsync()).EnabledFeatures &=
                ~HostFeatureFlags.RewardsAndRedemptions;
            _ = await db.SaveChangesAsync();
        }
        writes.Armed = true;
        (await f.Scenarios.RunAsync(draft, editor.Fixture, default))
            .ShouldBeOfType<AutomationScenarioRunOutcome.Invalid>()
            .Errors.ShouldContain(error => error.Code == "capability-unavailable");
        AutomationScenarioSerialization.Serialize(editor.Fixture).ShouldBe(baseline);
        (await f.Scenarios.ListAsync(new(f.HostId), flow, default))
            .ShouldHaveSingleItem()
            .Fixture.Context.Variables.ForExecution()
            .ShouldBeEmpty();
        writes.Writes.ShouldBe(0);
        f.Chat.Calls.ShouldBe(0);
    }
}
