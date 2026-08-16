using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationRuntimeTests
{
    [Test]
    public async Task AuthoringLifecycle_RoundTripsTypedGraphAndPositionsWithinSelectedHost()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""") with
        {
            Position = new(new(48), new(216)),
            DisplayAlias = "Chat command received",
        };
        var action = Node("send-chat", """{"message":"Welcome ${actor.display_name}!"}""") with
        {
            Position = new(new(600), new(72)),
            DisplayAlias = "Welcome the viewer in chat",
            InputBindings = Bindings(
                "message",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "actor.display_name")
            ),
        };
        var saved = (
            await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]) with
                {
                    IsEnabled = false,
                    Canvas = new(AutomationFlowOrientation.Vertical, AutomationEdgeStyle.Smooth),
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var otherHost = await fixture.SeedHostAsync("other-author", HostFeatureFlags.Automations);

        _ = (
            await fixture.Flows.DeleteAsync(new(otherHost), saved.FlowId, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowDeleteOutcome.FlowNotFound>();

        var loaded = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        var roundTrip = loaded.Flows.ShouldHaveSingleItem().Draft;
        roundTrip.Nodes.Single(node => node.Id == source.Id).Position.ShouldBe(source.Position);
        roundTrip
            .Nodes.Single(node => node.Id == source.Id)
            .DisplayAlias.ShouldBe(source.DisplayAlias);
        roundTrip
            .Nodes.Single(node => node.Id == action.Id)
            .Definition.Configuration.GetProperty("message")
            .GetString()
            .ShouldBe("Welcome ${actor.display_name}!");
        roundTrip
            .Nodes.Single(node => node.Id == action.Id)
            .InputBindings[new("message")]
            .ShouldBe(action.InputBindings[new("message")]);
        roundTrip.Edges.ShouldAllBe(static edge => edge.Kind == AutomationEdgeKind.Flow);
        roundTrip.Canvas.ShouldBe(
            new(AutomationFlowOrientation.Vertical, AutomationEdgeStyle.Smooth)
        );

        var moved = roundTrip with
        {
            Name = "Moved flow",
            Nodes = roundTrip
                .Nodes.Select(node =>
                    node.Id == action.Id ? node with { Position = new(new(648), new(96)) } : node
                )
                .ToImmutableArray(),
        };
        _ = (
            await fixture.Flows.SaveAsync(moved, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var afterUpdate = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        afterUpdate
            .Flows.ShouldHaveSingleItem()
            .Draft.Nodes.Single(node => node.Id == action.Id)
            .Position.ShouldBe(new(new(648), new(96)));

        var duplicated = (
            await fixture.Flows.DuplicateAsync(
                new(fixture.HostId),
                saved.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowDuplicateOutcome.Duplicated>();
        var afterDuplicate = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        var duplicate = afterDuplicate.Flows.Single(flow => flow.Draft.Id == duplicated.FlowId);
        duplicate
            .Draft.Nodes.Select(static node => node.DisplayAlias)
            .ShouldBe([source.DisplayAlias, action.DisplayAlias], ignoreOrder: true);

        _ = (
            await fixture.Flows.DeleteAsync(
                new(fixture.HostId),
                saved.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowDeleteOutcome.Deleted>();
        _ = (
            await fixture.Flows.DeleteAsync(
                new(fixture.HostId),
                duplicated.FlowId,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowDeleteOutcome.Deleted>();
        var afterDelete = (
            await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowQueryOutcome.Available>();
        afterDelete.Flows.ShouldBeEmpty();
    }

    [Test]
    public async Task SampleRun_EvaluatesTypedBranchWithoutEffectsOrDurableRun()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var condition = Node("condition", """{"expression":"viewer_count >= 20"}""");
        var action = Node("send-chat", """{"message":"Welcome ${actor.display_name}!"}""");

        var outcome = await fixture.Flows.RunSampleAsync(
            Draft(
                fixture.HostId,
                [source, condition, action],
                [Edge(source, "flow", condition), Edge(condition, "true", action)]
            ),
            source.Id,
            CancellationToken.None
        );

        var completed = outcome.ShouldBeOfType<AutomationSampleRunOutcome.Completed>();
        completed
            .Nodes.Select(static node => node.OutcomeCode)
            .ShouldBe(["source-received", "condition-true", "action-simulated"]);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Enablement_RejectsFlowWhoseSelectedCommandWasDeleted()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"hello"}""");
        var saved = (
            await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]) with
                {
                    IsEnabled = false,
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            _ = await db.CustomCommands.Where(command => command.Id == 7).ExecuteDeleteAsync();
        }

        var outcome = await fixture.Flows.SetEnabledAsync(
            new(fixture.HostId),
            saved.FlowId,
            enabled: true,
            CancellationToken.None
        );

        outcome
            .ShouldBeOfType<AutomationFlowEnableOutcome.Invalid>()
            .Errors.ShouldContain(static error =>
                error.Code == "custom-command-reference-unavailable"
            );
        await using var verified = await fixture.Database.CreateDbContextAsync();
        (await verified.AutomationFlows.SingleAsync()).IsEnabled.ShouldBeFalse();
    }

    [Test]
    public async Task FlowValidation_RejectsCyclesDisconnectedNodesAndIncompatibleEdges()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var first = Node("send-chat", """{"message":"first"}""");
        var second = Node("send-chat", """{"message":"second"}""");
        var joined = Node("send-chat", """{"message":"joined"}""");
        var disconnected = Node("delay", """{"duration-milliseconds":1000}""");
        var edges = ImmutableArray.Create(
            Edge(source, "actor", first),
            Edge(source, "flow", second),
            Edge(first, "complete", joined),
            Edge(second, "complete", joined),
            Edge(joined, "complete", first)
        );

        var outcome = await fixture.Flows.SaveAsync(
            Draft(fixture.HostId, [source, first, second, joined, disconnected], edges),
            CancellationToken.None
        );

        var invalid = outcome.ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        invalid.Errors.Select(static error => error.Code).ShouldContain("flow-cycle");
        invalid.Errors.Select(static error => error.Code).ShouldContain("node-disconnected");
        invalid.Errors.Select(static error => error.Code).ShouldContain("flow-port-incompatible");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task DataValidation_SeparatesTopologyAndEnforcesInputContracts()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var value = Node("test-number-value", "{}");
        var secondValue = Node("test-number-value", "{}");
        var first = ConnectedNode("test-number-consumer");
        var second = ConnectedNode("test-number-consumer");
        var fanOut = Draft(
            fixture.HostId,
            [source, value, first, second],
            [
                Edge(source, "flow", first),
                Edge(source, "flow", second),
                Edge(value, "value", first, "value", AutomationEdgeKind.Data),
                Edge(value, "value", second, "value", AutomationEdgeKind.Data),
            ]
        );

        _ = (
            await fixture.Flows.ValidateDraftAsync(fanOut, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();

        var duplicate = fanOut with
        {
            Nodes = [source, value, secondValue, first],
            Edges =
            [
                Edge(source, "flow", first),
                Edge(value, "value", first, "value", AutomationEdgeKind.Data),
                Edge(secondValue, "value", first, "value", AutomationEdgeKind.Data),
            ],
        };
        await AssertValidationCode(fixture, duplicate, "data-input-duplicate");

        var dataOnly = fanOut with
        {
            Nodes = [source, value, first],
            Edges = [Edge(value, "value", first, "value", AutomationEdgeKind.Data)],
        };
        await AssertValidationCode(fixture, dataOnly, "node-disconnected");

        var textValue = Node("test-text-value", "{}");
        var exactType = fanOut with
        {
            Nodes = [source, textValue, first],
            Edges =
            [
                Edge(source, "flow", first),
                Edge(textValue, "value", first, "value", AutomationEdgeKind.Data),
            ],
        };
        await AssertValidationCode(fixture, exactType, "data-type-incompatible");

        var nullableValue = Node("test-nullable-number-value", "{}");
        var nullRejected = fanOut with
        {
            Nodes = [source, nullableValue, first],
            Edges =
            [
                Edge(source, "flow", first),
                Edge(nullableValue, "value", first, "value", AutomationEdgeKind.Data),
            ],
        };
        await AssertValidationCode(fixture, nullRejected, "data-nullability-incompatible");

        var nullableConsumer = ConnectedNode("test-nullable-number-consumer");
        var nullAccepted = fanOut with
        {
            Nodes = [source, nullableValue, nullableConsumer],
            Edges =
            [
                Edge(source, "flow", nullableConsumer),
                Edge(nullableValue, "value", nullableConsumer, "value", AutomationEdgeKind.Data),
            ],
        };
        _ = (
            await fixture.Flows.ValidateDraftAsync(nullAccepted, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();

        var sensitiveValue = Node("test-sensitive-number-value", "{}");
        var sensitivity = fanOut with
        {
            Nodes = [source, sensitiveValue, first],
            Edges =
            [
                Edge(source, "flow", first),
                Edge(sensitiveValue, "value", first, "value", AutomationEdgeKind.Data),
            ],
        };
        await AssertValidationCode(fixture, sensitivity, "data-sensitivity-incompatible");

        var flowMisuse = fanOut with
        {
            Nodes = [source, value, first],
            Edges =
            [
                Edge(source, "flow", first),
                Edge(value, "value", first, "value", AutomationEdgeKind.Flow),
            ],
        };
        await AssertValidationCode(fixture, flowMisuse, "flow-port-incompatible");
    }

    [Test]
    public async Task DataValidation_RequiresEveryFlowPathAndRejectsCombinedCycles()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var firstSource = Node("test-number-source", "{}");
        var secondSource = Node("test-number-source", "{}");
        var consumer = ConnectedNode("test-number-consumer");
        var alternatePath = Draft(
            fixture.HostId,
            [firstSource, secondSource, consumer],
            [
                Edge(firstSource, "flow", consumer),
                Edge(secondSource, "flow", consumer),
                Edge(firstSource, "value", consumer, "value", AutomationEdgeKind.Data),
            ]
        );

        await AssertValidationCode(fixture, alternatePath, "data-source-unavailable");

        var onePath = alternatePath with
        {
            Edges =
            [
                Edge(firstSource, "flow", consumer),
                Edge(firstSource, "value", consumer, "value", AutomationEdgeKind.Data),
            ],
        };
        _ = (
            await fixture.Flows.ValidateDraftAsync(onePath, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();

        var control = Node(
            "test-data-control",
            """{"value":1}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );
        var producer = Node("test-data-control-output", "{}");
        var combined = Draft(
            fixture.HostId,
            [firstSource, control, producer],
            [
                Edge(firstSource, "flow", control),
                Edge(control, "complete", producer),
                Edge(producer, "value", control, "value", AutomationEdgeKind.Data),
            ]
        );
        var codes = await ValidationCodes(fixture, combined);
        codes.ShouldContain("dependency-cycle");
        codes.ShouldNotContain("flow-cycle");
        codes.ShouldNotContain("data-cycle");
    }

    [Test]
    public async Task DataEdges_DoNotScheduleNodesOrContributeFlowReachability()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var transform = ConnectedNode("test-number-transform");
        var action = Node("send-chat", """{"message":"flow only"}""");
        _ = await fixture.SaveAsync(
            [source, transform, action],
            [
                Edge(source, "value", transform, "value", AutomationEdgeKind.Data),
                Edge(source, "flow", action),
            ]
        );
        var context = Context(fixture.HostId) with
        {
            Event = new(Guid.NewGuid(), new("test-number-source")),
        };

        var outcome = await fixture.Runtime.DispatchAsync(
            new(context, new DataContractConfiguration()),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldBe(["flow only"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db
            .AutomationFlowRuns.Include(static candidate => candidate.NodeRuns)
            .SingleAsync();
        run.NodeRuns.Select(static node => node.NodeId)
            .ShouldBe([source.Id.Value, action.Id.Value], ignoreOrder: true);
    }

    [Test]
    public async Task MalformedPersistedBindings_BlockDispatchBeforeExternalAction()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var savedAction = await db.AutomationFlowNodes.SingleAsync(node =>
                node.Id == action.Id.Value
            );
            savedAction.InputBindingsJson = "{malformed";
            _ = await db.SaveChangesAsync();
        }

        var outcome = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.InvalidFlow);
        outcome.RunIds.ShouldBeEmpty();
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var verified = await fixture.Database.CreateDbContextAsync();
        (await verified.AutomationFlowRuns.CountAsync()).ShouldBe(0);
    }

    [Test]
    public void TypedNull_ContextSerializationPreservesTheDeclaredTypeAndSensitivity()
    {
        var context = Context(hostId: 42) with
        {
            Variables = new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("optional_number")] = new(
                        new AutomationValue.Null(AutomationPortValueType.Number),
                        AutomationDataSensitivity.Safe
                    ),
                }
            ),
        };

        var restored = AutomationRuntimeSerialization
            .RestoreContext(
                AutomationContextSchema.CurrentVersion,
                AutomationRuntimeSerialization.SerializeContext(context)
            )
            .ShouldBeOfType<AutomationContextRestoreOutcome.Available>();

        restored
            .Context.Variables.ForExecution()[new("optional_number")]
            .ShouldBe(
                new(
                    new AutomationValue.Null(AutomationPortValueType.Number),
                    AutomationDataSensitivity.Safe
                )
            );
    }

    [Test]
    public void TypedOutputs_RoundTripEveryValueKindOrderedArgumentsAndTypedNull()
    {
        var timestamp = new DateTimeOffset(2026, 8, 16, 10, 11, 12, TimeSpan.Zero);
        var outputs = new Dictionary<AutomationPortId, AutomationResolvedValue>
        {
            [new("text")] = new(
                new AutomationValue.Text("hello"),
                [AutomationValueProvenance.Generated]
            ),
            [new("number")] = new(
                new AutomationValue.Number(12.75m),
                [AutomationValueProvenance.Generated]
            ),
            [new("boolean")] = new(
                new AutomationValue.Boolean(true),
                [AutomationValueProvenance.Generated]
            ),
            [new("timestamp")] = new(
                new AutomationValue.Timestamp(timestamp),
                [AutomationValueProvenance.Generated]
            ),
            [new("actor")] = new(
                new AutomationValue.Actor(new("viewer", "Viewer")),
                [AutomationValueProvenance.PublicDisplayName, AutomationValueProvenance.PublicLogin]
            ),
            [new("channel")] = new(
                new AutomationValue.Channel(new("streamer", "Streamer")),
                [AutomationValueProvenance.PublicDisplayName, AutomationValueProvenance.PublicLogin]
            ),
            [new("stream")] = new(
                new AutomationValue.Stream(new("Title", "Game", timestamp)),
                [AutomationValueProvenance.Generated]
            ),
            [new("arguments")] = new(
                new AutomationValue.Arguments([
                    new(0, "first", [AutomationValueProvenance.PublicChat]),
                    new(1, "second", [AutomationValueProvenance.PublicChat]),
                ]),
                [AutomationValueProvenance.PublicChat]
            ),
            [new("null")] = new(
                new AutomationValue.Null(AutomationPortValueType.Number),
                [AutomationValueProvenance.Generated]
            ),
        };

        var json = AutomationDataValueSerialization.SerializeOutputs(outputs);
        var restored = AutomationDataValueSerialization
            .RestoreOutputs(json)
            .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>();

        restored.Outputs.Count.ShouldBe(outputs.Count);
        foreach (var (port, expected) in outputs)
        {
            var actual = restored.Outputs[port];
            actual.Provenance.ShouldBe(expected.Provenance);
            if (expected.Value is not AutomationValue.Arguments)
            {
                actual.Value.ShouldBe(expected.Value);
            }
        }

        restored
            .Outputs[new("arguments")]
            .Value.ShouldBeOfType<AutomationValue.Arguments>()
            .Values.Select(static argument => (argument.Position, argument.Value))
            .ShouldBe([(0, "first"), (1, "second")]);
        restored
            .Outputs[new("null")]
            .Value.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Number));
        var catalog = new AutomationDefinitionCatalog([new DataContractAutomationModule()]);
        _ = catalog.TryResolve(new("test-nullable-number-value"), out var nullableDefinition);
        AutomationPureHandlerRegistry
            .TryValidateResult(
                nullableDefinition.Descriptor,
                new(
                    ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                        new("value"),
                        new(
                            new AutomationValue.Null(AutomationPortValueType.Number),
                            [AutomationValueProvenance.Generated]
                        )
                    )
                ),
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty,
                out var validatedNull
            )
            .ShouldBeTrue();
        validatedNull[new("value")]
            .Value.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Number));
        _ = AutomationDataValueSerialization
            .RestoreOutputs("[{\"portId\":\"value\",\"value\":{\"kind\":\"unknown\"}}]")
            .ShouldBeOfType<AutomationOutputRestoreOutcome.Invalid>();
    }

    [Test]
    public void PureHandlerRegistry_RejectsDuplicatesDescriptorMismatchesAndEffectOutputs()
    {
        var catalog = new AutomationDefinitionCatalog([new DataContractAutomationModule()]);
        var handler = TextValueHandler("test-counting-text-value", "value");

        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            new AutomationPureHandlerRegistry(catalog, [handler, handler])
        );
        var mismatch = new TestPureHandler(
            handler.Contract with
            {
                Kind = AutomationNodeKind.Transform,
            },
            static _ => new AutomationPureNodeResult.Failed("unused")
        );
        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            new AutomationPureHandlerRegistry(catalog, [mismatch])
        );
        var sensitive = new TestPureHandler(
            new(
                new("test-sensitive-number-value"),
                AutomationNodeKind.Value,
                [],
                [
                    new(
                        new("value"),
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable
                    ),
                ]
            ),
            static _ => new AutomationPureNodeResult.Failed("unused")
        );
        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            new AutomationPureHandlerRegistry(catalog, [sensitive])
        );
        _ = Should.Throw<AutomationCatalogRegistrationException>(() =>
            new AutomationDefinitionCatalog([new ActionDataOutputModule()])
        );
    }

    [Test]
    public async Task PureOutput_ExecutesOnceAcrossFanOutAndRemainsHostIsolated()
    {
        var handler = TextValueHandler("test-counting-text-value", "shared-value");
        var transformHandler = TextTransformHandler();
        await using var fixture = await RuntimeFixture.CreateAsync(
            handlers: [handler, transformHandler]
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var value = Node("test-counting-text-value", "{}");
        var transform = Node(
            "test-text-transform",
            "{}",
            bindings: Bindings("input", AutomationInputBindingMode.Connected)
        );
        var first = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var second = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, value, transform, first, second],
            [
                Edge(source, "flow", first),
                Edge(source, "flow", second),
                Edge(value, "value", transform, "input", AutomationEdgeKind.Data),
                Edge(transform, "value", first, "message", AutomationEdgeKind.Data),
                Edge(transform, "value", second, "message", AutomationEdgeKind.Data),
            ]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        dispatched.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        handler.Calls.ShouldBe(1);
        transformHandler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["shared-value-transformed", "shared-value-transformed"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoints = await db
                .AutomationNodeRuns.Where(node =>
                    node.NodeId == value.Id.Value || node.NodeId == transform.Id.Value
                )
                .ToArrayAsync();
            checkpoints.Length.ShouldBe(2);
            checkpoints.ShouldAllBe(static checkpoint => checkpoint.OutputJson != null);
        }

        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        var diagnostic = summary
            .Nodes.Single(node => node.NodeId == value.Id)
            .Outputs.ShouldHaveSingleItem();
        diagnostic.ValueType.ShouldBe(AutomationPortValueType.Text);
        diagnostic.Provenance.ShouldBe([AutomationValueProvenance.Generated]);
        diagnostic.DisplayValue.ShouldBe("shared-value");

        var otherHost = await fixture.SeedHostAsync(
            "other",
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );
        (await fixture.Queries.ListAsync(new(otherHost), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldBeEmpty();
    }

    [Test]
    public async Task PureOutput_CheckpointBeforeDelayIsReusedAfterProcessRestart()
    {
        var handler = TextValueHandler("test-counting-text-value", "persisted-value");
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [handler]);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var value = Node("test-counting-text-value", "{}");
        var delay = Node(
            "test-data-delay",
            """{"duration-milliseconds":1000}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, value, delay, action],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", action),
                Edge(value, "value", delay, "value", AutomationEdgeKind.Data),
                Edge(value, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        handler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == value.Id.Value
            );
            checkpoint.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            checkpoint.OutputJson.ShouldNotBeNull().ShouldContain("persisted-value");
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await fixture
            .NewRuntime()
            .ResumeAsync(dispatched.RunIds.ShouldHaveSingleItem(), CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Completed);
        handler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["persisted-value"]);
    }

    [Test]
    public async Task DisableAfterCheckpoint_InvalidatesWithoutReplayOrReevaluation()
    {
        var handler = TextValueHandler("test-counting-text-value", "must-not-replay");
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [handler]);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var value = Node("test-counting-text-value", "{}");
        var delay = Node(
            "test-data-delay",
            """{"duration-milliseconds":1000}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, value, delay, action],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", action),
                Edge(value, "value", delay, "value", AutomationEdgeKind.Data),
                Edge(value, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        handler.Calls.ShouldBe(1);

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        (
            await fixture
                .NewRuntime()
                .ResumeAsync(dispatched.RunIds.ShouldHaveSingleItem(), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Invalidated);
        handler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task InterruptedDataAction_RetainsCheckpointAndDoesNotRetryEffectOrProducer()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = InterruptFirstSend(entered);
        var handler = TextValueHandler("test-counting-text-value", "single-attempt");
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat, handlers: [handler]);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var value = Node("test-counting-text-value", "{}");
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, value, action],
            [
                Edge(source, "flow", action),
                Edge(value, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );
        using var cancellation = new CancellationTokenSource();
        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            cancellation.Token
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runId = await SingleRunIdAsync(fixture);
        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => dispatch);

        (
            await fixture.NewRuntime().ResumeAsync(new(runId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Failed);
        handler.Calls.ShouldBe(1);
        chat.Messages.ShouldBe(["single-attempt"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
            node.NodeId == value.Id.Value
        );
        checkpoint.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
        _ = checkpoint.OutputJson.ShouldNotBeNull();
    }

    [Test]
    public async Task PureProducerFailure_IsRecordedAndConsumerPolicyControlsFlow()
    {
        var stopHandler = FailingTextValueHandler();
        await using var stop = await RuntimeFixture.CreateAsync(handlers: [stopHandler]);
        var stopSource = Node("custom-command", """{"custom-command-id":7}""");
        var stopValue = Node("test-failing-text-value", "{}");
        var stopConsumer = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await stop.SaveAsync(
            [stopSource, stopValue, stopConsumer],
            [
                Edge(stopSource, "flow", stopConsumer),
                Edge(stopValue, "value", stopConsumer, "message", AutomationEdgeKind.Data),
            ]
        );

        var stopped = await stop.Runtime.DispatchAsync(
            new(Context(stop.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        (
            await stop.Runtime.ResumeAsync(
                stopped.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Failed);
        stop.Chat.Messages.ShouldBeEmpty();
        await using (var db = await stop.Database.CreateDbContextAsync())
        {
            var nodes = await db.AutomationNodeRuns.ToArrayAsync();
            nodes
                .Single(node => node.NodeId == stopValue.Id.Value)
                .OutcomeCode.ShouldBe("deterministic-failure");
            nodes
                .Single(node => node.NodeId == stopConsumer.Id.Value)
                .OutcomeCode.ShouldBe("input-resolution-failed");
        }

        var continueHandler = FailingTextValueHandler();
        await using var continued = await RuntimeFixture.CreateAsync(handlers: [continueHandler]);
        var continueSource = Node("custom-command", """{"custom-command-id":7}""");
        var continueValue = Node("test-failing-text-value", "{}");
        var continueConsumer = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            AutomationNodeFailurePolicy.Continue,
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var after = Node("send-chat", """{"message":"after"}""");
        _ = await continued.SaveAsync(
            [continueSource, continueValue, continueConsumer, after],
            [
                Edge(continueSource, "flow", continueConsumer),
                Edge(continueConsumer, "complete", after),
                Edge(continueValue, "value", continueConsumer, "message", AutomationEdgeKind.Data),
            ]
        );

        var completed = await continued.Runtime.DispatchAsync(
            new(Context(continued.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        (
            await continued.Runtime.ResumeAsync(
                completed.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Completed);
        continued.Chat.Messages.ShouldBe(["after"]);
        var summary = (
            await continued.Queries.ListAsync(new(continued.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary
            .Nodes.Single(node => node.NodeId == continueConsumer.Id)
            .State.ShouldBe(AutomationNodeRunState.ContinuedAfterFailure);
    }

    [Test]
    public async Task MalformedCheckpoint_FailsClosedAndNeverExposesItsPayload()
    {
        var handler = TextValueHandler("test-counting-text-value", "safe-value");
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [handler]);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var value = Node("test-counting-text-value", "{}");
        var delay = Node(
            "test-data-delay",
            """{"duration-milliseconds":1000}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, value, delay, action],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", action),
                Edge(value, "value", delay, "value", AutomationEdgeKind.Data),
                Edge(value, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == value.Id.Value
            );
            checkpoint.OutputJson = "{malformed-private-payload";
            _ = await db.SaveChangesAsync();
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        var resumed = await fixture
            .NewRuntime()
            .ResumeAsync(dispatched.RunIds.ShouldHaveSingleItem(), CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        handler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == value.Id.Value
            );
            checkpoint.Status.ShouldBe(AutomationNodeRunStatus.Failed);
            checkpoint.OutcomeCode.ShouldBe("output-invalid");
            checkpoint.OutputJson.ShouldBeNull();
        }
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        JsonSerializer.Serialize(summary).ShouldNotContain("malformed-private-payload");
    }

    [Test]
    public async Task DisplayNameProjection_IsSafeButStableIdsAndPrivateValuesAreBlocked()
    {
        var display = DisplayNameHandler();
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [display]);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var projector = Node(
            "test-display-name-transform",
            "{}",
            bindings: Bindings("actor", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, projector, action],
            [
                Edge(source, "flow", action),
                Edge(source, "actor", projector, "actor", AutomationEdgeKind.Data),
                Edge(projector, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );

        _ = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        display.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["Viewer"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == projector.Id.Value
            );
            var outputJson = checkpoint.OutputJson.ShouldNotBeNull();
            outputJson.ShouldContain("Viewer");
            outputJson.ShouldNotContain("viewer-id");
        }
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary
            .Nodes.Single(node => node.NodeId == projector.Id)
            .Outputs.ShouldHaveSingleItem()
            .Provenance.ShouldBe([
                AutomationValueProvenance.Generated,
                AutomationValueProvenance.PublicDisplayName,
                AutomationValueProvenance.PublicLogin,
            ]);

        await using var stableId = await RuntimeFixture.CreateAsync();
        var idSource = Node("custom-command", """{"custom-command-id":7}""");
        var idAction = Node("send-chat", """{"message":"${actor.twitch_user_id}"}""");
        _ = await stableId.SaveAsync([idSource, idAction], [Edge(idSource, "flow", idAction)]);
        _ = await stableId.Runtime.DispatchAsync(
            new(Context(stableId.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        stableId.Chat.Messages.ShouldBeEmpty();

        await using var privateValue = await RuntimeFixture.CreateAsync();
        var privateSource = Node("custom-command", """{"custom-command-id":7}""");
        var privateAction = Node("send-chat", """{"message":"${private_value}"}""");
        _ = await privateValue.SaveAsync(
            [privateSource, privateAction],
            [Edge(privateSource, "flow", privateAction)]
        );
        _ = await privateValue.Runtime.DispatchAsync(
            new(Context(privateValue.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        privateValue.Chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task PublicChatArgumentsRemainSafeAndSampleDataResolutionHasNoEffectsOrRows()
    {
        await using (var arguments = await RuntimeFixture.CreateAsync())
        {
            var source = Node("custom-command", """{"custom-command-id":7}""");
            var action = Node("send-chat", """{"message":"${arguments[0]}"}""");
            _ = await arguments.SaveAsync([source, action], [Edge(source, "flow", action)]);
            _ = await arguments.Runtime.DispatchAsync(
                new(
                    Context(arguments.HostId, argument: "public words"),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            );
            arguments.Chat.Messages.ShouldBe(["public words"]);
        }

        var display = DisplayNameHandler();
        await using var sample = await RuntimeFixture.CreateAsync(handlers: [display]);
        var sampleSource = Node("custom-command", """{"custom-command-id":7}""");
        var projector = Node(
            "test-display-name-transform",
            "{}",
            bindings: Bindings("actor", AutomationInputBindingMode.Connected)
        );
        var actionNode = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var outcome = await sample.Flows.RunSampleAsync(
            Draft(
                sample.HostId,
                [sampleSource, projector, actionNode],
                [
                    Edge(sampleSource, "flow", actionNode),
                    Edge(sampleSource, "actor", projector, "actor", AutomationEdgeKind.Data),
                    Edge(projector, "value", actionNode, "message", AutomationEdgeKind.Data),
                ]
            ),
            sampleSource.Id,
            CancellationToken.None
        );

        var completed = outcome.ShouldBeOfType<AutomationSampleRunOutcome.Completed>();
        display.Calls.ShouldBe(1);
        completed
            .Nodes.Single(node => node.NodeId == actionNode.Id)
            .ResolvedInputs.ShouldHaveSingleItem()
            .DisplayValue.ShouldBe("available");
        sample.Chat.Messages.ShouldBeEmpty();
        await using var db = await sample.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task MalformedFrozenDefinition_FailsPendingRunWithoutExternalAction()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync(candidate =>
                candidate.Id == runId.Value
            );
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(run.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            frozen.Flow.Edges.ShouldAllBe(static edge => edge.Kind == AutomationEdgeKind.Flow);
            run.DefinitionJson = "{malformed";
            _ = await db.SaveChangesAsync();
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var verified = await fixture.Database.CreateDbContextAsync();
        var failed = await verified
            .AutomationFlowRuns.Include(static run => run.NodeRuns)
            .SingleAsync(run => run.Id == runId.Value);
        failed.Status.ShouldBe(AutomationFlowRunStatus.Failed);
        failed.NodeRuns.ShouldContain(static node => node.OutcomeCode == "definition-invalid");
    }

    [Test]
    public async Task FrozenDefinitionWithUnknownEdgePort_FailsPendingRunWithoutExternalAction()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync(candidate =>
                candidate.Id == runId.Value
            );
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(run.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            var mutated = frozen.Flow with
            {
                Edges = frozen
                    .Flow.Edges.Select(edge =>
                        edge.TargetNodeId == action.Id.Value
                            ? edge with
                            {
                                SourcePortId = "unknown-output",
                            }
                            : edge
                    )
                    .ToImmutableArray(),
            };
            var mutatedJson = JsonSerializer.Serialize(mutated, JsonSerializerOptions.Web);
            _ = AutomationRuntimeSerialization
                .RestoreDefinition(mutatedJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            run.DefinitionJson = mutatedJson;
            _ = await db.SaveChangesAsync();
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var verified = await fixture.Database.CreateDbContextAsync();
        var failed = await verified
            .AutomationFlowRuns.Include(static run => run.NodeRuns)
            .SingleAsync(run => run.Id == runId.Value);
        failed.Status.ShouldBe(AutomationFlowRunStatus.Failed);
        failed.NodeRuns.ShouldContain(static node => node.OutcomeCode == "definition-invalid");
    }

    [Test]
    public async Task FrozenDefinitionWithUnknownBindingField_FailsPendingRunWithoutExternalAction()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not send"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync(candidate =>
                candidate.Id == runId.Value
            );
            var frozen = AutomationRuntimeSerialization
                .RestoreDefinition(run.DefinitionJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            var mutated = frozen.Flow with
            {
                Nodes = frozen
                    .Flow.Nodes.Select(node =>
                        node.Id == action.Id.Value
                            ? node with
                            {
                                InputBindingsJson =
                                    AutomationRuntimeSerialization.SerializeInputBindings(
                                        Bindings("unknown-field", AutomationInputBindingMode.Fixed)
                                    ),
                            }
                            : node
                    )
                    .ToImmutableArray(),
            };
            var mutatedJson = JsonSerializer.Serialize(mutated, JsonSerializerOptions.Web);
            _ = AutomationRuntimeSerialization
                .RestoreDefinition(mutatedJson)
                .ShouldBeOfType<AutomationDefinitionRestoreOutcome.Available>();
            run.DefinitionJson = mutatedJson;
            _ = await db.SaveChangesAsync();
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var verified = await fixture.Database.CreateDbContextAsync();
        var failed = await verified
            .AutomationFlowRuns.Include(static run => run.NodeRuns)
            .SingleAsync(run => run.Id == runId.Value);
        failed.Status.ShouldBe(AutomationFlowRunStatus.Failed);
        failed.NodeRuns.ShouldContain(static node => node.OutcomeCode == "definition-invalid");
    }

    [Test]
    public async Task CelTransform_EffectiveDescriptorRoundTripsAndRetainsRepairableGraphState()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var configuration = TransformJson(
            [
                new(
                    "amount-input",
                    "amount",
                    "Amount",
                    "amount-binding",
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    12.5m
                ),
            ],
            [
                new(
                    "text-output",
                    "Formatted amount",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "format_number(amount, 2)"
                ),
            ]
        );
        var transform = Node(
            "test-cel-transform",
            configuration,
            bindings: Bindings(
                "amount-binding",
                AutomationInputBindingMode.Connected,
                new(AutomationExpressionLanguage.CurrentVersion, "'inactive-expression'")
            )
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var draft = Draft(
            fixture.HostId,
            [source, transform, action],
            [
                Edge(source, "flow", action),
                Edge(source, "value", transform, "amount-input", AutomationEdgeKind.Data),
                Edge(transform, "text-output", action, "message", AutomationEdgeKind.Data),
            ]
        ) with
        {
            IsEnabled = false,
        };

        var initialValidation = await fixture.Flows.ValidateDraftAsync(
            draft,
            CancellationToken.None
        );
        _ = initialValidation.ShouldBeOfType<AutomationFlowValidationOutcome.Valid>(
            JsonSerializer.Serialize(initialValidation)
        );
        var saved = (
            await fixture.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var restored = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(flow => flow.Draft.Id == saved.FlowId)
            .Draft;
        _ = (
            await fixture.Flows.SetEnabledAsync(
                new(fixture.HostId),
                saved.FlowId,
                enabled: true,
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowEnableOutcome.Updated>();
        var restoredTransform = restored.Nodes.Single(node => node.Id == transform.Id);
        restoredTransform.Definition.Configuration.GetRawText().ShouldBe(configuration);
        restoredTransform.InputBindings.ShouldBe(transform.InputBindings);
        restored.Edges.ShouldBe(draft.Edges, ignoreOrder: true);

        var effective = fixture
            .Catalog.ValidatePersistedDefinition(restoredTransform.Definition)
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        effective.Definition.Inputs.ShouldHaveSingleItem().Id.ShouldBe(new("amount-input"));
        effective.Definition.Inputs[0].BindingFieldId.ShouldBe(new("amount-binding"));
        effective.Definition.Outputs.ShouldHaveSingleItem().Id.ShouldBe(new("text-output"));
        fixture
            .Catalog.TryDescribe(new("test-text-value"), out var staticDescriptor)
            .ShouldBeTrue();
        staticDescriptor.Outputs.ShouldHaveSingleItem().Id.ShouldBe(new("value"));

        var renamedJson = TransformJson(
            [
                new(
                    "amount-input",
                    "amount",
                    "Renamed amount",
                    "amount-binding",
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    99m
                ),
            ],
            [
                new(
                    "text-output",
                    "Renamed result",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "format_number(amount, 2)"
                ),
            ]
        );
        var renamed = restored with
        {
            Nodes = restored
                .Nodes.Select(node =>
                    node.Id == transform.Id
                        ? Node("test-cel-transform", renamedJson, bindings: node.InputBindings) with
                        {
                            Id = node.Id,
                        }
                        : node
                )
                .ToImmutableArray(),
        };
        _ = (
            await fixture.Flows.ValidateDraftAsync(renamed, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();
        renamed.Edges.ShouldBe(restored.Edges, ignoreOrder: true);

        var removedInput = renamed with
        {
            Nodes = renamed
                .Nodes.Select(node =>
                    node.Id == transform.Id
                        ? Node(
                            "test-cel-transform",
                            TransformJson(
                                [],
                                [
                                    new(
                                        "text-output",
                                        "Renamed result",
                                        AutomationPortValueType.Text,
                                        AutomationPortNullability.NonNullable,
                                        "'literal'"
                                    ),
                                ]
                            ),
                            bindings: node.InputBindings
                        ) with
                        {
                            Id = node.Id,
                        }
                        : node
                )
                .ToImmutableArray(),
        };
        var removedErrors = (
            await fixture.Flows.ValidateDraftAsync(removedInput, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
        removedErrors.Errors.Select(static error => error.Code).ShouldContain("port-missing");
        removedErrors
            .Errors.Select(static error => error.Code)
            .ShouldContain("binding-field-invalid");
        removedInput.Edges.ShouldBe(renamed.Edges, ignoreOrder: true);
        removedInput
            .Nodes.Single(node => node.Id == transform.Id)
            .InputBindings.ShouldContainKey(new("amount-binding"));

        var changedContracts = renamed with
        {
            Nodes = renamed
                .Nodes.Select(node =>
                    node.Id == transform.Id
                        ? Node(
                            "test-cel-transform",
                            TransformJson(
                                [
                                    new(
                                        "amount-input",
                                        "amount",
                                        "Amount",
                                        "amount-binding",
                                        AutomationPortValueType.Text,
                                        AutomationPortNullability.NonNullable,
                                        "12.5"
                                    ),
                                ],
                                [
                                    new(
                                        "text-output",
                                        "Result",
                                        AutomationPortValueType.Text,
                                        AutomationPortNullability.Nullable,
                                        "amount"
                                    ),
                                ]
                            ),
                            bindings: node.InputBindings
                        ) with
                        {
                            Id = node.Id,
                        }
                        : node
                )
                .ToImmutableArray(),
        };
        var changedErrors = (
            await fixture.Flows.ValidateDraftAsync(changedContracts, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
        changedErrors
            .Errors.Select(static error => error.Code)
            .ShouldContain("data-type-incompatible");
        changedErrors
            .Errors.Select(static error => error.Code)
            .ShouldContain("data-nullability-incompatible");
        changedContracts.Edges.ShouldBe(renamed.Edges, ignoreOrder: true);

        var duplicateIdentity = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "same-port",
                        "format_number",
                        "Invalid",
                        "same-field",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        1m
                    ),
                ],
                [
                    new(
                        "same-port",
                        "Invalid",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'x'"
                    ),
                ]
            )
        );
        _ = fixture
            .Catalog.ValidatePersistedDefinition(duplicateIdentity.Definition)
            .ShouldBeOfType<AutomationConfigurationCheck.Invalid>();
    }

    [Test]
    public void CelTransform_FoundationKeepsSafeContractsOffThePublicCatalogSurface()
    {
        var constructor = typeof(AutomationDefinition<DataContractConfiguration>)
            .GetConstructors()
            .ShouldHaveSingleItem();
        constructor.GetParameters().Length.ShouldBe(3);
        typeof(AutomationSafeTriggerViewDescriptor).IsPublic.ShouldBeFalse();
        typeof(AutomationSafeTriggerViewField).IsPublic.ShouldBeFalse();
    }

    [Test]
    [Arguments("replacement", "amount-binding")]
    [Arguments("amount", "replacement-binding")]
    public async Task CelTransform_UpdateRejectsRetainedInputIdentityReplacementAndPreservesStoredGraph(
        string identifier,
        string bindingFieldId
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "amount-input",
                        "amount",
                        "Amount",
                        "amount-binding",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        1m
                    ),
                ],
                [
                    new(
                        "text-output",
                        "Text",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "format_number(amount, 2)"
                    ),
                ]
            ),
            bindings: Bindings("amount-binding", AutomationInputBindingMode.Connected)
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var flowId = await fixture.SaveAsync(
            [source, transform, action],
            [
                Edge(source, "flow", action),
                Edge(source, "value", transform, "amount-input", AutomationEdgeKind.Data),
                Edge(transform, "text-output", action, "message", AutomationEdgeKind.Data),
            ]
        );
        var before = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(flow => flow.Draft.Id == flowId);
        var replacementConfiguration = TransformJson(
            [
                new(
                    "amount-input",
                    identifier,
                    "Amount",
                    bindingFieldId,
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    1m
                ),
            ],
            [
                new(
                    "text-output",
                    "Text",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    $"format_number({identifier}, 2)"
                ),
            ]
        );
        var update = before.Draft with
        {
            Name = "Must not persist",
            Nodes = before
                .Draft.Nodes.Select(node =>
                    node.Id == transform.Id
                        ? Node(
                            "test-cel-transform",
                            replacementConfiguration,
                            bindings: Bindings(bindingFieldId, AutomationInputBindingMode.Connected)
                        ) with
                        {
                            Id = node.Id,
                        }
                        : node
                )
                .ToImmutableArray(),
        };
        _ = (
            await fixture.Flows.ValidateDraftAsync(update, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();

        var rejected = (
            await fixture.Flows.SaveAsync(update, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>();
        var error = rejected.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("transform-input-identity-changed");
        error.NodeId.ShouldBe(transform.Id);
        error.PortId.ShouldBe(new("amount-input"));

        var after = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(flow => flow.Draft.Id == flowId);
        after.Draft.Name.ShouldBe(before.Draft.Name);
        after.UpdatedAtUtc.ShouldBe(before.UpdatedAtUtc);
        after.Draft.Edges.ShouldBe(before.Draft.Edges, ignoreOrder: true);
        var original = before.Draft.Nodes.Single(node => node.Id == transform.Id);
        var retained = after.Draft.Nodes.Single(node => node.Id == transform.Id);
        retained
            .Definition.Configuration.GetRawText()
            .ShouldBe(original.Definition.Configuration.GetRawText());
        retained.InputBindings.ShouldBe(original.InputBindings);
    }

    [Test]
    public async Task CelTransform_RestrictedInputAdmissionUsesArgumentsOnlyView()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "arguments-input",
                        "input_arguments",
                        "Arguments",
                        "arguments-binding",
                        AutomationPortValueType.Arguments,
                        AutomationPortNullability.NonNullable,
                        Array.Empty<string>()
                    ),
                ],
                [
                    new(
                        "text-output",
                        "Text",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "input_arguments[0]"
                    ),
                ]
            ),
            bindings: Bindings(
                "arguments-binding",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "arguments")
            )
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var draft = Draft(
            fixture.HostId,
            [source, transform, action],
            [
                Edge(source, "flow", action),
                Edge(transform, "text-output", action, "message", AutomationEdgeKind.Data),
            ]
        ) with
        {
            IsEnabled = false,
        };

        _ = (
            await fixture.Flows.ValidateDraftAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();
        var sample = (
            await fixture.Flows.RunSampleAsync(draft, source.Id, CancellationToken.None)
        ).ShouldBeOfType<AutomationSampleRunOutcome.Completed>();
        sample
            .Nodes.Single(node => node.NodeId == action.Id)
            .ResolvedInputs.ShouldHaveSingleItem()
            .DisplayValue.ShouldBe("available");

        foreach (
            var rejected in new[]
            {
                "actor",
                "actor.display_name",
                "channel",
                "channel.login",
                "stream",
                "stream.title",
                "event.source",
                "timestamps.occurred_at",
                "target_id",
                "private_value",
                "input_arguments",
                "text_output",
                "services.send('x')",
            }
        )
        {
            var invalid = draft with
            {
                Nodes = draft
                    .Nodes.Select(node =>
                        node.Id == transform.Id
                            ? node with
                            {
                                InputBindings = Bindings(
                                    "arguments-binding",
                                    AutomationInputBindingMode.Expression,
                                    new(AutomationExpressionLanguage.CurrentVersion, rejected)
                                ),
                            }
                            : node
                    )
                    .ToImmutableArray(),
            };
            var errors = (
                await fixture.Flows.ValidateDraftAsync(invalid, CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>();
            var diagnostic = errors.Errors.Single(error =>
                error.Code == "binding-expression-unavailable"
            );
            diagnostic.FieldId.ShouldBe(new("arguments-binding"));
            diagnostic.PortId.ShouldBe(new("arguments-input"));
            diagnostic.SafeTriggerFieldId.ShouldBeNull();
            diagnostic.Message.ShouldNotContain("Viewer");
            diagnostic.Message.ShouldNotContain("private");
        }

        var literalTransform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "literal-input",
                        "literal",
                        "Literal",
                        "literal-binding",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "fallback"
                    ),
                ],
                [
                    new(
                        "literal-output",
                        "Literal",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "literal"
                    ),
                ]
            ),
            bindings: Bindings(
                "literal-binding",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "'actor.display_name'")
            )
        );
        var literalDraft = draft with
        {
            Nodes = draft.Nodes.Replace(transform, literalTransform),
            Edges =
            [
                Edge(source, "flow", action),
                Edge(
                    literalTransform,
                    "literal-output",
                    action,
                    "message",
                    AutomationEdgeKind.Data
                ),
            ],
        };
        _ = (
            await fixture.Flows.ValidateDraftAsync(literalDraft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();

        var secondSource = Node("custom-command", """{"custom-command-id":7}""");
        var everyPath = draft with
        {
            Nodes = draft.Nodes.Add(secondSource),
            Edges = draft.Edges.Add(Edge(secondSource, "flow", action)),
        };
        _ = (
            await fixture.Flows.ValidateDraftAsync(everyPath, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowValidationOutcome.Valid>();

        var directGlobal = Node(
            "test-cel-transform",
            TransformJson(
                [],
                [
                    new(
                        "invalid-output",
                        "Invalid",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "arguments[0]"
                    ),
                ]
            )
        );
        _ = fixture
            .Catalog.ValidatePersistedDefinition(directGlobal.Definition)
            .ShouldBeOfType<AutomationConfigurationCheck.Invalid>();

        var wrongInputType = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "text-input",
                        "text",
                        "Text",
                        "text-binding",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "fallback"
                    ),
                ],
                [
                    new(
                        "text-output",
                        "Text",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "text"
                    ),
                ]
            ),
            bindings: Bindings(
                "text-binding",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "arguments")
            )
        );
        var wrongTypeDraft = draft with
        {
            Nodes = draft.Nodes.Replace(transform, wrongInputType),
            Edges =
            [
                Edge(source, "flow", action),
                Edge(wrongInputType, "text-output", action, "message", AutomationEdgeKind.Data),
            ],
        };
        await AssertValidationCode(fixture, wrongTypeDraft, "binding-expression-unavailable");
    }

    [Test]
    public void CelTransform_ArgumentsDescriptorPreservesOrderClassificationAndStaticProvenance()
    {
        var descriptor = AutomationSafeTriggerView.Descriptor;
        descriptor.Version.ShouldBe(AutomationSafeTriggerView.CurrentVersion);
        descriptor.Fields.ShouldBe(
            [
                new(
                    new("arguments"),
                    "arguments",
                    AutomationPortValueType.Arguments,
                    AutomationPortNullability.NonNullable,
                    AutomationValueProvenance.PublicChat
                ),
            ],
            ignoreOrder: false
        );
        var service = new AutomationSafeTriggerExpressionService();
        var port = new AutomationPortMetadata(
            new("arguments-input"),
            "Arguments",
            "Receives ordered Safe arguments.",
            AutomationPortValueType.Arguments,
            Nullability: AutomationPortNullability.NonNullable,
            BindingFieldId: new("arguments-binding")
        );
        var context = Context(1, sensitive: "must-not-escape") with
        {
            Actor = new("actor-id", "actor-login", "Actor Display"),
            Stream = new("stream-id", "Stream Title", "Stream Game", DateTimeOffset.UtcNow),
            Arguments = [new(2, "third"), new(0, "first"), new(1, "second")],
            Variables = new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("source_arguments")] = new(
                        new AutomationValue.Arguments([
                            new(0, "unproven", [AutomationValueProvenance.Generated]),
                        ]),
                        AutomationDataSensitivity.Safe
                    ),
                    [new("private_arguments")] = new(
                        new AutomationValue.Arguments([
                            new(0, "must-not-escape", [AutomationValueProvenance.PublicChat]),
                        ]),
                        AutomationDataSensitivity.Sensitive
                    ),
                }
            ),
        };

        var resolved = service
            .Evaluate(new(AutomationExpressionLanguage.CurrentVersion, "arguments"), port, context)
            .ShouldNotBeNull();
        var arguments = resolved.Value.ShouldBeOfType<AutomationValue.Arguments>();
        arguments
            .Values.Select(static argument => (argument.Position, argument.Value))
            .ShouldBe([(0, "first"), (1, "second"), (2, "third")], ignoreOrder: false);
        foreach (var argument in arguments.Values)
        {
            argument.Provenance.ShouldBe([AutomationValueProvenance.PublicChat]);
        }
        resolved.Provenance.ShouldBe([
            AutomationValueProvenance.Generated,
            AutomationValueProvenance.PublicChat,
        ]);
        resolved.SafeTriggerFields.ShouldBe([new AutomationSafeTriggerFieldId("arguments")]);

        var empty = service
            .Evaluate(
                new(AutomationExpressionLanguage.CurrentVersion, "arguments"),
                port,
                context with
                {
                    Arguments = [],
                }
            )
            .ShouldNotBeNull();
        empty.Value.ShouldBeOfType<AutomationValue.Arguments>().Values.ShouldBeEmpty();
        empty.Provenance.ShouldBe(resolved.Provenance);
        empty.SafeTriggerFields.ShouldBe(resolved.SafeTriggerFields);

        var untaken = service
            .Evaluate(
                new(AutomationExpressionLanguage.CurrentVersion, "false ? arguments : null"),
                port with
                {
                    Nullability = AutomationPortNullability.Nullable,
                },
                context
            )
            .ShouldNotBeNull();
        untaken.Value.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Arguments));
        untaken.Provenance.ShouldBe(resolved.Provenance);
        untaken.SafeTriggerFields.ShouldBe(resolved.SafeTriggerFields);

        foreach (
            var unavailable in new[]
            {
                "actor.display_name",
                "channel.login",
                "stream.title",
                "source_arguments",
                "private_arguments",
            }
        )
        {
            service
                .Evaluate(
                    new(AutomationExpressionLanguage.CurrentVersion, unavailable),
                    port,
                    context
                )
                .ShouldBeNull();
        }
    }

    [Test]
    public void CelTransform_EvaluatesOnlyExactScalarOutputsAndTypedNull()
    {
        var timestamp = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var configuration = new AutomationCelTransformConfiguration(
            [
                new(
                    new("actor-input"),
                    new("safe_actor"),
                    "Actor",
                    new("actor-binding"),
                    AutomationPortValueType.Actor,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Actor(new("viewer", "Viewer"))
                ),
            ],
            [
                new(
                    new("text"),
                    "Text",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "'value'"
                ),
                new(
                    new("number"),
                    "Number",
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    "1"
                ),
                new(
                    new("boolean"),
                    "Boolean",
                    AutomationPortValueType.Boolean,
                    AutomationPortNullability.NonNullable,
                    "true"
                ),
                new(
                    new("timestamp"),
                    "Timestamp",
                    AutomationPortValueType.Timestamp,
                    AutomationPortNullability.NonNullable,
                    $"timestamp('{timestamp:O}')"
                ),
                new(
                    new("nullable"),
                    "Nullable",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.Nullable,
                    "null"
                ),
                new(
                    new("actor-name"),
                    "Actor name",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "safe_actor.display_name"
                ),
            ]
        );

        var outputs = new AutomationTransformCelService()
            .Execute(
                configuration,
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                    new("actor-input"),
                    new(
                        new AutomationValue.Actor(new("viewer", "Viewer")),
                        [AutomationValueProvenance.Generated]
                    )
                )
            )
            .ShouldBeOfType<AutomationPureNodeResult.Succeeded>()
            .Outputs;
        outputs[new("text")].Value.ShouldBe(new AutomationValue.Text("value"));
        outputs[new("number")].Value.ShouldBe(new AutomationValue.Number(1m));
        outputs[new("boolean")].Value.ShouldBe(new AutomationValue.Boolean(true));
        outputs[new("timestamp")].Value.ShouldBe(new AutomationValue.Timestamp(timestamp));
        outputs[new("nullable")]
            .Value.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Text));
        outputs[new("actor-name")].Value.ShouldBe(new AutomationValue.Text("Viewer"));
        outputs.ShouldAllBe(static output =>
            output.Value.Provenance.Length == 1
            && output.Value.Provenance[0] == AutomationValueProvenance.Generated
        );
    }

    [Test]
    public async Task CelTransform_ConnectedCompositesExposeOnlyClosedSafeMembers()
    {
        var timestamp = new DateTimeOffset(2026, 8, 17, 10, 11, 12, TimeSpan.Zero);
        await using var fixture = await RuntimeFixture.CreateAsync(
            handlers:
            [
                CompositeValueHandler(
                    "test-stream-value",
                    AutomationPortValueType.Stream,
                    new AutomationValue.Stream(new("Title", "Game", timestamp))
                ),
            ]
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var stream = Node("test-stream-value", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "actor-input",
                        "actor",
                        "Actor",
                        "actor-binding",
                        AutomationPortValueType.Actor,
                        AutomationPortNullability.NonNullable,
                        new Dictionary<string, object?>
                        {
                            ["login"] = "fallback",
                            ["display-name"] = "Fallback",
                        }
                    ),
                    new(
                        "channel-input",
                        "channel",
                        "Channel",
                        "channel-binding",
                        AutomationPortValueType.Channel,
                        AutomationPortNullability.NonNullable,
                        new Dictionary<string, object?>
                        {
                            ["login"] = "fallback",
                            ["display-name"] = "Fallback",
                        }
                    ),
                    new(
                        "stream-input",
                        "stream",
                        "Stream",
                        "stream-binding",
                        AutomationPortValueType.Stream,
                        AutomationPortNullability.NonNullable,
                        new Dictionary<string, object?>
                        {
                            ["title"] = "Fallback",
                            ["game-name"] = "Fallback",
                            ["started-at"] = timestamp,
                        }
                    ),
                    new(
                        "arguments-input",
                        "input_arguments",
                        "Arguments",
                        "arguments-binding",
                        AutomationPortValueType.Arguments,
                        AutomationPortNullability.NonNullable,
                        Array.Empty<string>()
                    ),
                ],
                [
                    new(
                        "actor-name",
                        "Actor name",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "actor.display_name"
                    ),
                    new(
                        "channel-name",
                        "Channel name",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "channel.display_name"
                    ),
                    new(
                        "stream-title",
                        "Stream title",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.Nullable,
                        "stream.title"
                    ),
                    new(
                        "first-argument",
                        "First argument",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "input_arguments[0]"
                    ),
                ]
            ),
            bindings: Bindings("actor-binding", AutomationInputBindingMode.Connected)
                .Add(
                    new("channel-binding"),
                    new(AutomationInputBindingMode.Connected, Expression: null)
                )
                .Add(
                    new("stream-binding"),
                    new(AutomationInputBindingMode.Connected, Expression: null)
                )
                .Add(
                    new("arguments-binding"),
                    new(AutomationInputBindingMode.Connected, Expression: null)
                )
        );
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, stream, transform, action],
            [
                Edge(source, "flow", action),
                Edge(source, "actor", transform, "actor-input", AutomationEdgeKind.Data),
                Edge(source, "channel", transform, "channel-input", AutomationEdgeKind.Data),
                Edge(stream, "value", transform, "stream-input", AutomationEdgeKind.Data),
                Edge(source, "arguments", transform, "arguments-input", AutomationEdgeKind.Data),
                Edge(transform, "actor-name", action, "message", AutomationEdgeKind.Data),
            ]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, argument: "first"),
                new CustomCommandSourceConfiguration(new(7))
            ),
            CancellationToken.None
        );
        dispatched.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.Chat.Messages.ShouldBe(["Viewer"]);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == transform.Id.Value
            );
            var outputs = AutomationDataValueSerialization
                .RestoreOutputs(checkpoint.OutputJson!)
                .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>()
                .Outputs;
            outputs[new("actor-name")].Value.ShouldBe(new AutomationValue.Text("Viewer"));
            outputs[new("channel-name")].Value.ShouldBe(new AutomationValue.Text("Host 1"));
            outputs[new("stream-title")].Value.ShouldBe(new AutomationValue.Text("Title"));
            outputs[new("first-argument")].Value.ShouldBe(new AutomationValue.Text("first"));
            outputs.Values.ShouldAllBe(static output =>
                output.Provenance.Length == 4
                && output.Provenance[0] == AutomationValueProvenance.Generated
                && output.Provenance[1] == AutomationValueProvenance.PublicDisplayName
                && output.Provenance[2] == AutomationValueProvenance.PublicLogin
                && output.Provenance[3] == AutomationValueProvenance.PublicChat
                && output.SafeTriggerFields.IsEmpty
                && output.ValueFreeDiagnostic
            );
        }

        var configuration = fixture
            .Catalog.ValidatePersistedDefinition(transform.Definition)
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>()
            .Configuration.ShouldBeOfType<AutomationCelTransformConfiguration>();
        var declaredInputs = configuration.Inputs.ToImmutableDictionary(
            static input => input.Identifier.Value,
            StringComparer.Ordinal
        );
        foreach (
            var unavailable in new[]
            {
                "actor.twitch_user_id",
                "channel.host_id",
                "stream.twitch_stream_id",
                "input_arguments.private_value",
                "arguments[0]",
            }
        )
        {
            AutomationTransformCelService
                .ValidateOutput(
                    new(
                        new("invalid"),
                        "Invalid",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.Nullable,
                        unavailable
                    ),
                    declaredInputs
                )
                .ShouldBeFalse();
        }
    }

    [Test]
    [NotInParallel]
    public void CelTransform_FormatNumberUsesInvocationCultureAndExactLongPrecision()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var declaredInputs = new[]
        {
            new AutomationCelTransformInput(
                new("amount-input"),
                new("amount"),
                "Amount",
                new("amount-binding"),
                AutomationPortValueType.Number,
                AutomationPortNullability.NonNullable,
                new AutomationValue.Number(1m)
            ),
            new AutomationCelTransformInput(
                new("precision-input"),
                new("precision"),
                "Precision",
                new("precision-binding"),
                AutomationPortValueType.Number,
                AutomationPortNullability.NonNullable,
                new AutomationValue.Number(2m)
            ),
        }.ToImmutableDictionary(static input => input.Identifier.Value, StringComparer.Ordinal);
        foreach (var precision in new[] { "0", "2", "6" })
        {
            AutomationTransformCelService
                .ValidateOutput(
                    new(
                        new("output"),
                        "Output",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        $"format_number(amount, {precision})"
                    ),
                    declaredInputs
                )
                .ShouldBeTrue();
        }
        foreach (var precision in new[] { "-1", "7", "2.0", "null", "'2'" })
        {
            AutomationTransformCelService
                .ValidateOutput(
                    new(
                        new("output"),
                        "Output",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        $"format_number(amount, {precision})"
                    ),
                    declaredInputs
                )
                .ShouldBeFalse();
        }
        foreach (
            var source in new[]
            {
                "format_number(amount, int(precision))",
                "value ${format_number(amount, int(precision))}",
            }
        )
        {
            AutomationTransformCelService
                .ValidateOutput(
                    new(
                        new("output"),
                        "Output",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        source
                    ),
                    declaredInputs
                )
                .ShouldBeTrue();
        }

        try
        {
            var service = new AutomationTransformCelService();
            var inputs = ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                new("amount-input"),
                new(new AutomationValue.Number(-1234.5m), [AutomationValueProvenance.Generated])
            );
            foreach (var (culture, general) in new[] { ("en-GB", "-1234.5"), ("de-DE", "-1234,5") })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var precision in Enumerable.Range(0, 7))
                {
                    var configuration = TransformConfiguration(
                        "format_number(amount)",
                        $"format_number(amount, {precision})"
                    );
                    var outputs = service
                        .Execute(configuration, inputs)
                        .ShouldBeOfType<AutomationPureNodeResult.Succeeded>()
                        .Outputs;
                    outputs[new("first-output")].Value.ShouldBe(new AutomationValue.Text(general));
                    outputs[new("second-output")]
                        .Value.ShouldBe(
                            new AutomationValue.Text(
                                (-1234.5m).ToString($"F{precision}", CultureInfo.CurrentCulture)
                            )
                        );
                }
                var interpolated = service
                    .Execute(
                        TransformConfiguration("value ${format_number(amount, 2)}", "'unused'"),
                        inputs
                    )
                    .ShouldBeOfType<AutomationPureNodeResult.Succeeded>();
                interpolated
                    .Outputs[new("first-output")]
                    .Value.ShouldBe(
                        new AutomationValue.Text(
                            $"value {(-1234.5m).ToString("F2", CultureInfo.CurrentCulture)}"
                        )
                    );
            }

            var dynamicInputs = inputs.Add(
                new("precision-input"),
                new(new AutomationValue.Number(2m), [AutomationValueProvenance.Generated])
            );
            var dynamicOutputs = service
                .Execute(
                    TransformDynamicPrecisionConfiguration(
                        "format_number(amount, int(precision))",
                        "value ${format_number(amount, int(precision))}"
                    ),
                    dynamicInputs
                )
                .ShouldBeOfType<AutomationPureNodeResult.Succeeded>()
                .Outputs;
            dynamicOutputs[new("first-output")]
                .Value.ShouldBe(
                    new AutomationValue.Text((-1234.5m).ToString("F2", CultureInfo.CurrentCulture))
                );
            dynamicOutputs[new("second-output")]
                .Value.ShouldBe(
                    new AutomationValue.Text(
                        $"value {(-1234.5m).ToString("F2", CultureInfo.CurrentCulture)}"
                    )
                );
            foreach (var precision in new[] { -1m, 7m })
            {
                var outOfRange = inputs.Add(
                    new("precision-input"),
                    new(
                        new AutomationValue.Number(precision),
                        [AutomationValueProvenance.Generated]
                    )
                );
                _ = service
                    .Execute(
                        TransformDynamicPrecisionConfiguration(
                            "format_number(amount, int(precision))",
                            "'unused'"
                        ),
                        outOfRange
                    )
                    .ShouldBeOfType<AutomationPureNodeResult.Failed>();
                _ = service
                    .Execute(
                        TransformDynamicPrecisionConfiguration(
                            "'unused'",
                            "value ${format_number(amount, int(precision))}"
                        ),
                        outOfRange
                    )
                    .ShouldBeOfType<AutomationPureNodeResult.Failed>();
            }

            foreach (var precision in new[] { "-1", "7", "2.0", "null", "'2'" })
            {
                var invalid = TransformConfiguration(
                    $"format_number(amount, {precision})",
                    "'unused'"
                );
                _ = service
                    .Execute(invalid, inputs)
                    .ShouldBeOfType<AutomationPureNodeResult.Failed>();
            }

            _ = service
                .Execute(TransformConfiguration("'first'", "1"), inputs)
                .ShouldBeOfType<AutomationPureNodeResult.Failed>();
            foreach (var invalidNumber in new[] { "null", "true", "'1'" })
            {
                _ = service
                    .Execute(
                        TransformConfiguration($"format_number({invalidNumber})", "'unused'"),
                        inputs
                    )
                    .ShouldBeOfType<AutomationPureNodeResult.Failed>();
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            _ = Should.Throw<OperationCanceledException>(() =>
                service.Execute(
                    TransformConfiguration("'first'", "'second'"),
                    inputs,
                    cancelled.Token
                )
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void CelTransform_ResultValidationRequiresExactGeneratedInputProvenance()
    {
        var descriptor = new AutomationDefinitionDescriptor(
            new("test-cel-transform"),
            AutomationNodeKind.Transform,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            new("Transform", "Validates exact provenance.", "Test"),
            [
                new(
                    new("input"),
                    "Input",
                    "Input",
                    AutomationPortValueType.Text,
                    BindingFieldId: new("input-binding")
                ),
            ],
            [new(new("output"), "Output", "Output", AutomationPortValueType.Text)],
            [
                new(
                    new("input-binding"),
                    "Input",
                    "Input",
                    new AutomationConfigurationFieldType.Data(AutomationPortValueType.Text),
                    true
                ),
            ],
            AutomationActionCapabilities.None,
            AutomationActionRetrySafety.NotApplicable
        );
        var inputs = ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
            new("input"),
            new(new AutomationValue.Text("safe"), [AutomationValueProvenance.PublicLogin])
        );

        foreach (
            var provenance in new ImmutableArray<AutomationValueProvenance>[]
            {
                [AutomationValueProvenance.PublicLogin],
                [AutomationValueProvenance.Generated],
                [AutomationValueProvenance.PublicLogin, AutomationValueProvenance.Generated],
                [
                    AutomationValueProvenance.Generated,
                    AutomationValueProvenance.PublicLogin,
                    AutomationValueProvenance.PublicLogin,
                ],
                [
                    AutomationValueProvenance.Generated,
                    AutomationValueProvenance.PublicLogin,
                    AutomationValueProvenance.PublicChat,
                ],
            }
        )
        {
            AutomationPureHandlerRegistry
                .TryValidateResult(
                    descriptor,
                    new(
                        ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                            new("output"),
                            new(new AutomationValue.Text("safe"), provenance)
                        )
                    ),
                    inputs,
                    out _
                )
                .ShouldBeFalse();
        }

        AutomationPureHandlerRegistry
            .TryValidateResult(
                descriptor,
                new(
                    ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                        new("output"),
                        new(
                            new AutomationValue.Text("safe"),
                            [
                                AutomationValueProvenance.Generated,
                                AutomationValueProvenance.PublicLogin,
                            ]
                        )
                    )
                ),
                inputs,
                out var validated
            )
            .ShouldBeTrue();
        validated.ShouldContainKey(new("output"));
    }

    [Test]
    public async Task CelTransform_FrozenArgumentsViewCheckpointsAndRestoresAcrossLiveSchemaChange()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "arguments-input",
                        "input_arguments",
                        "Arguments",
                        "arguments-binding",
                        AutomationPortValueType.Arguments,
                        AutomationPortNullability.NonNullable,
                        Array.Empty<string>()
                    ),
                ],
                [
                    new(
                        "first-output",
                        "First",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "input_arguments[0]"
                    ),
                    new(
                        "second-output",
                        "Second",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'frozen-second'"
                    ),
                ]
            ),
            bindings: Bindings(
                "arguments-binding",
                AutomationInputBindingMode.Expression,
                new(AutomationExpressionLanguage.CurrentVersion, "arguments")
            )
        );
        var delay = Node(
            "test-data-delay",
            """{"duration-milliseconds":1000}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );
        var first = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var second = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var saved = await fixture.SaveAsync(
            [source, transform, delay, first, second],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", first),
                Edge(delay, "complete", second),
                Edge(transform, "first-output", delay, "value", AutomationEdgeKind.Data),
                Edge(transform, "first-output", first, "message", AutomationEdgeKind.Data),
                Edge(transform, "second-output", second, "message", AutomationEdgeKind.Data),
            ]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(
                Context(
                    fixture.HostId,
                    argument: "frozen-first",
                    sourceDefinitionId: new("test-number-source")
                ),
                new DataContractConfiguration()
            ),
            CancellationToken.None
        );
        dispatched.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.TransformHandler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                node.NodeId == transform.Id.Value
            );
            var restored = AutomationDataValueSerialization
                .RestoreOutputs(checkpoint.OutputJson!)
                .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>();
            restored.Outputs.Keys.ShouldBe(
                [new AutomationPortId("first-output"), new("second-output")],
                ignoreOrder: true
            );
            restored
                .Outputs[new("first-output")]
                .Provenance.ShouldBe([
                    AutomationValueProvenance.Generated,
                    AutomationValueProvenance.PublicChat,
                ]);
            restored
                .Outputs[new("first-output")]
                .SafeTriggerFields.ShouldBe([new AutomationSafeTriggerFieldId("arguments")]);
        }
        var diagnostics = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem()
            .Nodes.Single(node => node.NodeId == transform.Id)
            .Outputs;
        diagnostics.ShouldAllBe(static diagnostic => diagnostic.DisplayValue == "available");
        JsonSerializer.Serialize(diagnostics).ShouldNotContain("frozen-first");
        JsonSerializer.Serialize(diagnostics).ShouldNotContain("frozen-second");

        var live = (await fixture.Flows.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single(flow => flow.Draft.Id == saved)
            .Draft;
        var changedConfiguration = TransformJson(
            [
                new(
                    "arguments-input",
                    "input_arguments",
                    "Arguments",
                    "arguments-binding",
                    AutomationPortValueType.Arguments,
                    AutomationPortNullability.NonNullable,
                    new[] { "live-first" }
                ),
            ],
            [
                new(
                    "first-output",
                    "First",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "input_arguments[0]"
                ),
                new(
                    "second-output",
                    "Second",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    "'live-second'"
                ),
            ]
        );
        var changed = live with
        {
            Nodes = live
                .Nodes.Select(node =>
                    node.Id == source.Id
                        ? Node("custom-command", """{"custom-command-id":7}""") with
                        {
                            Id = node.Id,
                        }
                    : node.Id == transform.Id
                        ? Node(
                            "test-cel-transform",
                            changedConfiguration,
                            bindings: Bindings(
                                "arguments-binding",
                                AutomationInputBindingMode.Fixed,
                                node.InputBindings[new("arguments-binding")].Expression
                            )
                        ) with
                        {
                            Id = node.Id,
                        }
                    : node
                )
                .ToImmutableArray(),
        };
        _ = (
            await fixture.Flows.SaveAsync(changed, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await fixture
            .NewRuntime()
            .ResumeAsync(dispatched.RunIds.ShouldHaveSingleItem(), CancellationToken.None);
        resumed.Status.ShouldBe(AutomationResumeStatus.Completed);
        fixture.TransformHandler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBe(["frozen-first", "frozen-second"], ignoreOrder: true);

        async Task AssertMalformedCheckpointAsync(
            Func<
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>,
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>
            > mutate
        )
        {
            var malformedDispatch = await fixture.Runtime.DispatchAsync(
                new(
                    Context(fixture.HostId, argument: "live-context"),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            );
            malformedDispatch.Status.ShouldBe(AutomationDispatchStatus.Accepted);
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                var malformedRunId = malformedDispatch.RunIds.ShouldHaveSingleItem().Value;
                var checkpoint = await db.AutomationNodeRuns.SingleAsync(node =>
                    node.RunId == malformedRunId && node.NodeId == transform.Id.Value
                );
                var outputs = AutomationDataValueSerialization
                    .RestoreOutputs(checkpoint.OutputJson!)
                    .ShouldBeOfType<AutomationOutputRestoreOutcome.Available>()
                    .Outputs;
                checkpoint.OutputJson = AutomationDataValueSerialization.SerializeOutputs(
                    mutate(outputs)
                );
                _ = await db.SaveChangesAsync();
            }

            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            var malformedResume = await fixture
                .NewRuntime()
                .ResumeAsync(
                    malformedDispatch.RunIds.ShouldHaveSingleItem(),
                    CancellationToken.None
                );
            malformedResume.Status.ShouldBe(AutomationResumeStatus.Failed);
            fixture.Chat.Messages.ShouldBe(["frozen-first", "frozen-second"], ignoreOrder: true);
        }

        await AssertMalformedCheckpointAsync(outputs =>
            outputs.Add(
                new("extra-output"),
                new(
                    new AutomationValue.Text("must-not-escape"),
                    [AutomationValueProvenance.Generated],
                    ValueFreeDiagnostic: true
                )
            )
        );
        await AssertMalformedCheckpointAsync(outputs => outputs.Remove(new("second-output")));
        await AssertMalformedCheckpointAsync(outputs =>
            outputs.SetItem(
                new("first-output"),
                outputs[new("first-output")] with
                {
                    Provenance =
                    [
                        AutomationValueProvenance.Generated,
                        AutomationValueProvenance.PublicChat,
                    ],
                }
            )
        );
        fixture.TransformHandler.Calls.ShouldBe(4);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CelTransform_LeaseLossAfterMultiOutputEvaluationBeforeCheckpointHonorsScheduledFailurePolicy(
        bool continueOnFailure
    )
    {
        var outputsReady = new TaskCompletionSource<AutomationPureNodeResult.Succeeded>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await RuntimeFixture.CreateAsync(
            transformDecorator: handler => new PausingPureHandler(
                handler,
                outputsReady,
                release.Task
            )
        );
        var policy = continueOnFailure
            ? AutomationNodeFailurePolicy.Continue
            : AutomationNodeFailurePolicy.Stop;
        var source = Node("test-number-source", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "name-input",
                        "name",
                        "Name",
                        "name-binding",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "Viewer"
                    ),
                ],
                [
                    new(
                        "first-output",
                        "First",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "name"
                    ),
                    new(
                        "second-output",
                        "Second",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'second'"
                    ),
                ]
            ),
            bindings: Bindings("name-binding", AutomationInputBindingMode.Fixed)
        ) with
        {
            FailurePolicy = policy,
        };
        var consumer = Node(
            "test-text-consumer",
            """{"message":"must not send"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        ) with
        {
            FailurePolicy = policy,
        };
        var continued = Node("condition", """{"expression":"arguments[0] == 'yes'"}""");
        _ = await fixture.SaveAsync(
            [source, transform, consumer, continued],
            [
                Edge(source, "flow", consumer),
                Edge(consumer, "complete", continued),
                Edge(transform, "first-output", consumer, "message", AutomationEdgeKind.Data),
            ]
        );

        var dispatch = fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, sourceDefinitionId: new("test-number-source")),
                new DataContractConfiguration()
            ),
            CancellationToken.None
        );
        var computed = await outputsReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
        computed.Outputs.Keys.ShouldBe(
            [new AutomationPortId("first-output"), new("second-output")],
            ignoreOrder: true
        );
        fixture.TransformHandler.Calls.ShouldBe(1);

        var foreignLease = Guid.NewGuid();
        Guid runId;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db
                .AutomationFlowRuns.Include(static value => value.NodeRuns)
                .SingleAsync();
            runId = run.Id;
            _ = run.ExecutionLeaseId.ShouldNotBeNull();
            run.NodeRuns.Single(node => node.NodeId == transform.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Running);
            run.NodeRuns.Single(node => node.NodeId == consumer.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Running);
            run.NodeRuns.ShouldNotContain(node => node.NodeId == continued.Id.Value);
            _ = await db
                .AutomationFlowRuns.Where(value => value.Id == runId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(static value => value.ExecutionLeaseId, _ => foreignLease)
                );
        }
        release.SetResult();
        (await dispatch.WaitAsync(TimeSpan.FromSeconds(5))).Status.ShouldBe(
            AutomationDispatchStatus.Accepted
        );

        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db
                .AutomationFlowRuns.AsNoTracking()
                .Include(static value => value.NodeRuns)
                .SingleAsync();
            run.Status.ShouldBe(AutomationFlowRunStatus.Running);
            run.ExecutionLeaseId.ShouldBe(foreignLease);
            var checkpoint = run.NodeRuns.Single(node => node.NodeId == transform.Id.Value);
            checkpoint.Status.ShouldBe(AutomationNodeRunStatus.Running);
            checkpoint.OutcomeCode.ShouldBeNull();
            checkpoint.OutputJson.ShouldBeNull();
            checkpoint.CompletedAtUtc.ShouldBeNull();
            var scheduled = run.NodeRuns.Single(node => node.NodeId == consumer.Id.Value);
            scheduled.Status.ShouldBe(AutomationNodeRunStatus.Running);
            scheduled.OutcomeCode.ShouldBeNull();
            scheduled.CompletedAtUtc.ShouldBeNull();
            run.NodeRuns.ShouldNotContain(node => node.NodeId == continued.Id.Value);
        }
        fixture.Chat.Messages.ShouldBeEmpty();
        var diagnostics = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem()
            .Nodes.Single(node => node.NodeId == transform.Id)
            .Outputs;
        diagnostics.ShouldBeEmpty();

        var resumed = await fixture.NewRuntime().ResumeAsync(new(runId), CancellationToken.None);
        resumed.Status.ShouldBe(
            continueOnFailure ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        fixture.TransformHandler.Calls.ShouldBe(1);
        await using var verified = await fixture.Database.CreateDbContextAsync();
        var recovered = await verified
            .AutomationFlowRuns.AsNoTracking()
            .Include(static value => value.NodeRuns)
            .SingleAsync();
        recovered.Status.ShouldBe(
            continueOnFailure ? AutomationFlowRunStatus.Completed : AutomationFlowRunStatus.Failed
        );
        var recoveredCheckpoint = recovered.NodeRuns.Single(node =>
            node.NodeId == transform.Id.Value
        );
        recoveredCheckpoint.OutputJson.ShouldBeNull();
        recoveredCheckpoint.Status.ShouldBe(
            continueOnFailure
                ? AutomationNodeRunStatus.ContinuedAfterFailure
                : AutomationNodeRunStatus.Failed
        );
        var recoveredConsumer = recovered.NodeRuns.Single(node => node.NodeId == consumer.Id.Value);
        recoveredConsumer.Status.ShouldBe(recoveredCheckpoint.Status);
        if (continueOnFailure)
        {
            var continuation = recovered.NodeRuns.Single(node => node.NodeId == continued.Id.Value);
            continuation.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
            continuation.OutcomeCode.ShouldBe("condition-true");
        }
        else
        {
            recovered.NodeRuns.ShouldNotContain(node => node.NodeId == continued.Id.Value);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CelTransform_LaterOutputFailureExposesNoPartialCheckpointAndHonorsScheduledFailurePolicy(
        bool continueOnFailure
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("test-number-source", "{}");
        var transform = Node(
            "test-cel-transform",
            TransformJson(
                [
                    new(
                        "amount-input",
                        "amount",
                        "Amount",
                        "amount-binding",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        -1234.5m
                    ),
                    new(
                        "precision-input",
                        "precision",
                        "Precision",
                        "precision-binding",
                        AutomationPortValueType.Number,
                        AutomationPortNullability.NonNullable,
                        7m
                    ),
                ],
                [
                    new(
                        "first-output",
                        "First",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "'must-not-escape'"
                    ),
                    new(
                        "second-output",
                        "Second",
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable,
                        "format_number(amount, int(precision))"
                    ),
                ]
            ),
            bindings: Bindings("amount-binding", AutomationInputBindingMode.Fixed)
                .Add(
                    new("precision-binding"),
                    new(AutomationInputBindingMode.Fixed, Expression: null)
                )
        );
        var consumer = Node(
            "test-text-consumer",
            """{"message":"must not send"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        ) with
        {
            FailurePolicy = continueOnFailure
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var continued = Node("condition", """{"expression":"arguments[0] == 'yes'"}""");
        _ = await fixture.SaveAsync(
            [source, transform, consumer, continued],
            [
                Edge(source, "flow", consumer),
                Edge(consumer, "complete", continued),
                Edge(transform, "first-output", consumer, "message", AutomationEdgeKind.Data),
            ]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, sourceDefinitionId: new("test-number-source")),
                new DataContractConfiguration()
            ),
            CancellationToken.None
        );

        dispatched.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        fixture.TransformHandler.Calls.ShouldBe(1);
        fixture.Chat.Messages.ShouldBeEmpty();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var run = await db
                .AutomationFlowRuns.AsNoTracking()
                .Include(static value => value.NodeRuns)
                .SingleAsync();
            run.Status.ShouldBe(
                continueOnFailure
                    ? AutomationFlowRunStatus.Completed
                    : AutomationFlowRunStatus.Failed
            );
            var checkpoint = run.NodeRuns.Single(node => node.NodeId == transform.Id.Value);
            checkpoint.Status.ShouldBe(AutomationNodeRunStatus.Failed);
            checkpoint.OutcomeCode.ShouldBe("output-invalid");
            checkpoint.OutputJson.ShouldBeNull();
            run.NodeRuns.ShouldNotContain(static node => node.OutcomeCode == "output-checkpointed");
            var scheduled = run.NodeRuns.Single(node => node.NodeId == consumer.Id.Value);
            scheduled.Status.ShouldBe(
                continueOnFailure
                    ? AutomationNodeRunStatus.ContinuedAfterFailure
                    : AutomationNodeRunStatus.Failed
            );
            scheduled.OutcomeCode.ShouldBe("input-resolution-failed");
            if (continueOnFailure)
            {
                var continuation = run.NodeRuns.Single(node => node.NodeId == continued.Id.Value);
                continuation.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
                continuation.OutcomeCode.ShouldBe("condition-true");
            }
            else
            {
                run.NodeRuns.ShouldNotContain(node => node.NodeId == continued.Id.Value);
            }
        }

        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.Nodes.Single(node => node.NodeId == transform.Id).Outputs.ShouldBeEmpty();
        JsonSerializer.Serialize(summary).ShouldNotContain("must-not-escape");
    }

    [Test]
    public async Task MultipleMatchingTriggers_StartIndependentRunsThroughSharedNode()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var firstSource = Node("custom-command", """{"custom-command-id":7}""");
        var secondSource = Node("custom-command", """{"custom-command-id":7}""");
        var shared = Node("send-chat", """{"message":"shared"}""");
        _ = await fixture.SaveAsync(
            [firstSource, secondSource, shared],
            [Edge(firstSource, "flow", shared), Edge(secondSource, "flow", shared)]
        );
        var trigger = new AutomationTrigger(
            Context(fixture.HostId),
            new CustomCommandSourceConfiguration(new(7))
        );

        var first = await fixture.Runtime.DispatchAsync(trigger, CancellationToken.None);
        var duplicate = await fixture.Runtime.DispatchAsync(trigger, CancellationToken.None);

        first.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        first.RunIds.Length.ShouldBe(2);
        duplicate.Status.ShouldBe(AutomationDispatchStatus.Duplicate);
        fixture.Chat.Messages.ShouldBe(["shared", "shared"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var runs = await db
            .AutomationFlowRuns.Include(static run => run.NodeRuns)
            .OrderBy(static run => run.SourceNodeId)
            .ToArrayAsync();
        runs.Select(static run => run.SourceNodeId)
            .ShouldBe([firstSource.Id.Value, secondSource.Id.Value], ignoreOrder: true);
        runs.ShouldAllBe(run => run.NodeRuns.Count(node => node.NodeId == shared.Id.Value) == 1);
    }

    [Test]
    public async Task Dispatch_ConditionsRouteDeterministicallyAndExternalIdentityDeduplicates()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var condition = Node("condition", """{"expression":"arguments[0] == 'yes'"}""");
        var matched = Node("send-chat", """{"message":"Hello ${actor.display_name}"}""");
        var unmatched = Node("send-chat", """{"message":"No match"}""");
        var flowId = await fixture.SaveAsync(
            [source, condition, matched, unmatched],
            [
                Edge(source, "flow", condition),
                Edge(condition, "true", matched),
                Edge(condition, "false", unmatched),
            ]
        );
        var context = Context(fixture.HostId, "yes", "private-argument");

        var first = await fixture.Runtime.DispatchAsync(
            new(context, new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var duplicate = await fixture.Runtime.DispatchAsync(
            new(context, new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        first.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        first.RunIds.Length.ShouldBe(1);
        duplicate.Status.ShouldBe(AutomationDispatchStatus.Duplicate);
        fixture.Chat.Messages.ShouldBe(["Hello Viewer"]);
        var query = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        var summary = query.Runs.ShouldHaveSingleItem();
        summary.FlowId.ShouldBe(flowId);
        summary.State.ShouldBe(AutomationFlowRunState.Completed);
        JsonSerializer.Serialize(summary).ShouldNotContain("private-argument");
        summary.Nodes.ShouldAllBe(static node =>
            node.OutcomeCode == "source-received"
            || node.OutcomeCode == "condition-true"
            || node.OutcomeCode == "action-succeeded"
        );
    }

    [Test]
    public async Task ConcurrentDispatch_SameOccurrenceCreatesOneRunAndExecutesOneAction()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"once"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var trigger = new AutomationTrigger(
            Context(fixture.HostId),
            new CustomCommandSourceConfiguration(new(7))
        );
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<AutomationDispatchOutcome> DispatchAsync()
        {
            await start.Task;
            return await fixture.Runtime.DispatchAsync(trigger, CancellationToken.None);
        }

        var first = DispatchAsync();
        var second = DispatchAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        outcomes
            .Select(static outcome => outcome.Status)
            .Order()
            .ShouldBe([AutomationDispatchStatus.Accepted, AutomationDispatchStatus.Duplicate]);
        fixture.Chat.Messages.ShouldBe(["once"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
        (
            await db.AutomationNodeRuns.CountAsync(static node =>
                node.OutcomeCode == "action-succeeded"
            )
        ).ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentResumeAndWorker_MultipleBranchesExecuteSeriallyAndOnlyOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(
            null,
            async (call, _, cancellationToken) =>
            {
                if (call != 1)
                {
                    return;
                }

                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        );
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var first = Node("send-chat", """{"message":"first"}""");
        var second = Node("send-chat", """{"message":"second"}""");
        _ = await fixture.SaveAsync(
            [source, first, second],
            [Edge(source, "flow", first), Edge(source, "flow", second)]
        );

        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Guid runId;
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            runId = await db.AutomationFlowRuns.Select(static run => run.Id).SingleAsync();
        }

        var concurrentResume = fixture.Runtime.ResumeAsync(new(runId), CancellationToken.None);
        var worker = fixture.Runtime.ResumeDueAsync(CancellationToken.None);
        (await concurrentResume.WaitAsync(TimeSpan.FromSeconds(5))).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        chat.Messages.Count.ShouldBe(1);

        release.SetResult();
        _ = await dispatch.WaitAsync(TimeSpan.FromSeconds(5));

        chat.Messages.Order().ShouldBe(["first", "second"]);
        await using var verified = await fixture.Database.CreateDbContextAsync();
        var actionRuns = await verified
            .AutomationNodeRuns.Where(node =>
                node.NodeId == first.Id.Value || node.NodeId == second.Id.Value
            )
            .ToArrayAsync();
        actionRuns.Length.ShouldBe(2);
        actionRuns.ShouldAllBe(static node =>
            node.Status == AutomationNodeRunStatus.Succeeded
            && node.OutcomeCode == "action-succeeded"
        );
    }

    [Test]
    public async Task DisableDuringAction_InvalidationRemainsTerminalAndEnqueuesNoContinuation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(
            null,
            async (call, _, cancellationToken) =>
            {
                if (call != 1)
                {
                    return;
                }

                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        );
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"in flight"}""");
        var never = Node("send-chat", """{"message":"must not replay"}""");
        _ = await fixture.SaveAsync(
            [source, action, never],
            [Edge(source, "flow", action), Edge(action, "complete", never)]
        );
        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runId = await SingleRunIdAsync(fixture);

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        release.SetResult();
        _ = await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );

        (
            await fixture.NewRuntime().ResumeAsync(new(runId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Invalidated);
        chat.Messages.ShouldBe(["in flight"]);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.Include(static value => value.NodeRuns).SingleAsync();
        run.Status.ShouldBe(AutomationFlowRunStatus.Invalidated);
        run.NodeRuns.ShouldNotContain(static node =>
            node.Status == AutomationNodeRunStatus.Pending
            || node.Status == AutomationNodeRunStatus.Running
        );
        run.NodeRuns.ShouldNotContain(node => node.NodeId == never.Id.Value);
    }

    [Test]
    public async Task DurableDelay_NewRuntimeResumesPersistedContinuationAfterRestart()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"After restart"}""");
        var flowId = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        var changedAction = action with
        {
            Definition = Persisted("send-chat", """{"message":"Changed after the run started"}"""),
        };
        _ = (
            await fixture.Flows.SaveAsync(
                Draft(
                    fixture.HostId,
                    [source, delay, changedAction],
                    [Edge(source, "flow", delay), Edge(delay, "complete", changedAction)]
                ) with
                {
                    Id = flowId,
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var restarted = fixture.NewRuntime();

        var resumed = await restarted.ResumeAsync(
            dispatched.RunIds.ShouldHaveSingleItem(),
            CancellationToken.None
        );

        resumed.Status.ShouldBe(AutomationResumeStatus.Completed);
        fixture.Chat.Messages.ShouldBe(["After restart"]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task DurableContext_UnsupportedSchemaTerminatesGenericallyWithoutExecution(
        int unsupportedVersion
    )
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not execute"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            _ = await db
                .AutomationFlowRuns.Where(run => run.Id == runId.Value)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(static run => run.ContextSchemaVersion, unsupportedVersion)
                );
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));

        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Failed);
        fixture.Chat.Messages.ShouldBeEmpty();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.Nodes.ShouldContain(static node =>
            node.OutcomeCode == "context-version-unsupported"
        );
        JsonSerializer.Serialize(summary).ShouldNotContain("must not execute");
    }

    [Test]
    public async Task FailurePolicies_StopOrContinueWithoutAutomaticRetries()
    {
        await using var stop = await RuntimeFixture.CreateAsync([false, true]);
        var stopSource = Node("custom-command", """{"custom-command-id":7}""");
        var stopFailure = Node("send-chat", """{"message":"reject"}""");
        var never = Node("send-chat", """{"message":"never"}""");
        _ = await stop.SaveAsync(
            [stopSource, stopFailure, never],
            [Edge(stopSource, "flow", stopFailure), Edge(stopFailure, "complete", never)]
        );

        var stopped = await stop.Runtime.DispatchAsync(
            new(Context(stop.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        stop.Chat.Messages.ShouldBe(["reject"]);
        (
            await stop.Runtime.ResumeAsync(
                stopped.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Failed);

        await using var continued = await RuntimeFixture.CreateAsync([false, true]);
        var continueSource = Node("custom-command", """{"custom-command-id":7}""");
        var continueFailure = Node(
            "send-chat",
            """{"message":"reject"}""",
            AutomationNodeFailurePolicy.Continue
        );
        var after = Node("send-chat", """{"message":"after"}""");
        _ = await continued.SaveAsync(
            [continueSource, continueFailure, after],
            [
                Edge(continueSource, "flow", continueFailure),
                Edge(continueFailure, "complete", after),
            ]
        );

        var completed = await continued.Runtime.DispatchAsync(
            new(Context(continued.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        var continuedSummary = (
            await continued.Queries.ListAsync(new(continued.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        continuedSummary
            .Nodes.Select(static node => (node.State, node.OutcomeCode))
            .ShouldBe([
                (AutomationNodeRunState.Succeeded, "source-received"),
                (AutomationNodeRunState.ContinuedAfterFailure, "chat-rejected"),
                (AutomationNodeRunState.Succeeded, "action-succeeded"),
            ]);
        continuedSummary.FailedNode?.NodeId.ShouldBe(continueFailure.Id);
        continued.Chat.Messages.ShouldBe(["reject", "after"]);
        (
            await continued.Runtime.ResumeAsync(
                completed.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Completed);
    }

    [Test]
    public async Task InterruptedActions_RestartAppliesStopOrContinueWithoutRetryingTheAction()
    {
        var stopEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var stopChat = InterruptFirstSend(stopEntered);
        await using var stop = await RuntimeFixture.CreateAsync(chat: stopChat);
        var stopSource = Node("custom-command", """{"custom-command-id":7}""");
        var stopAction = Node("send-chat", """{"message":"stop interrupted"}""");
        _ = await stop.SaveAsync([stopSource, stopAction], [Edge(stopSource, "flow", stopAction)]);
        using var stopCancellation = new CancellationTokenSource();
        var stopDispatch = stop.Runtime.DispatchAsync(
            new(Context(stop.HostId), new CustomCommandSourceConfiguration(new(7))),
            stopCancellation.Token
        );
        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopRunId = await SingleRunIdAsync(stop);
        stopCancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => stopDispatch);

        (
            await stop.NewRuntime().ResumeAsync(new(stopRunId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Failed);
        stopChat.Messages.ShouldBe(["stop interrupted"]);

        var continueEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var continueChat = InterruptFirstSend(continueEntered);
        await using var continued = await RuntimeFixture.CreateAsync(chat: continueChat);
        var continueSource = Node("custom-command", """{"custom-command-id":7}""");
        var continueAction = Node(
            "send-chat",
            """{"message":"continue interrupted"}""",
            AutomationNodeFailurePolicy.Continue
        );
        var after = Node("send-chat", """{"message":"after restart"}""");
        _ = await continued.SaveAsync(
            [continueSource, continueAction, after],
            [Edge(continueSource, "flow", continueAction), Edge(continueAction, "complete", after)]
        );
        using var continueCancellation = new CancellationTokenSource();
        var continueDispatch = continued.Runtime.DispatchAsync(
            new(Context(continued.HostId), new CustomCommandSourceConfiguration(new(7))),
            continueCancellation.Token
        );
        await continueEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var continueRunId = await SingleRunIdAsync(continued);
        continueCancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => continueDispatch);

        (
            await continued.NewRuntime().ResumeAsync(new(continueRunId), CancellationToken.None)
        ).Status.ShouldBe(AutomationResumeStatus.Completed);
        continueChat.Messages.ShouldBe(["continue interrupted", "after restart"]);
        var summary = (
            await continued.Queries.ListAsync(new(continued.HostId), CancellationToken.None)
        )
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary
            .Nodes.Single(node => node.NodeId == continueAction.Id)
            .State.ShouldBe(AutomationNodeRunState.ContinuedAfterFailure);
        summary
            .Nodes.Single(node => node.NodeId == after.Id)
            .State.ShouldBe(AutomationNodeRunState.Succeeded);
    }

    [Test]
    public async Task Restart_WithInterruptedStopAndContinueBranches_StopRemainsTerminal()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var stopDelay = Node("delay", """{"duration-milliseconds":1000}""");
        var continueDelay = Node("delay", """{"duration-milliseconds":1000}""");
        var stopAction = Node("send-chat", """{"message":"must not retry"}""");
        var continueAction = Node(
            "send-chat",
            """{"message":"must not continue"}""",
            AutomationNodeFailurePolicy.Continue
        );
        _ = await fixture.SaveAsync(
            [source, stopDelay, continueDelay, stopAction, continueAction],
            [
                Edge(source, "flow", stopDelay),
                Edge(source, "flow", continueDelay),
                Edge(stopDelay, "complete", stopAction),
                Edge(continueDelay, "complete", continueAction),
            ]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            _ = await db
                .AutomationNodeRuns.Where(node =>
                    node.RunId == runId.Value
                    && (
                        node.NodeId == stopAction.Id.Value || node.NodeId == continueAction.Id.Value
                    )
                )
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(static node => node.Status, AutomationNodeRunStatus.Running)
                        .SetProperty(
                            static node => node.StartedAtUtc,
                            fixture.Clock.GetUtcNow().UtcDateTime
                        )
                );
            _ = await db
                .AutomationFlowRuns.Where(run => run.Id == runId.Value)
                .ExecuteUpdateAsync(setters =>
                    setters
                        .SetProperty(static run => run.Status, AutomationFlowRunStatus.Running)
                        .SetProperty(static run => run.ExecutionLeaseId, Guid.NewGuid())
                );
        }

        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Failed
        );

        fixture.Chat.Messages.ShouldBeEmpty();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.State.ShouldBe(AutomationFlowRunState.Failed);
        summary
            .Nodes.Single(node => node.NodeId == stopAction.Id)
            .OutcomeCode.ShouldBe("execution-interrupted");
        summary
            .Nodes.Single(node => node.NodeId == continueAction.Id)
            .OutcomeCode.ShouldBe("flow-stopped");
    }

    [Test]
    public async Task Disable_InvalidatesPendingWorkAndReenableNeverReplaysIt()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not replay"}""");
        var flowId = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        var blocked = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        blocked.Status.ShouldBe(AutomationDispatchStatus.FeatureDisabled);
        _ = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.FeatureDisabled>();

        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        var resumed = await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None);

        resumed.Status.ShouldBe(AutomationResumeStatus.Invalidated);
        fixture.Chat.Messages.ShouldBeEmpty();
        var query = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        query.Runs.ShouldHaveSingleItem().State.ShouldBe(AutomationFlowRunState.Invalidated);
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.SingleAsync()).Id.ShouldBe(flowId.Value);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task SecondaryFeatureDisable_InvalidatesAffectedWorkButRetainsAuthorisedHistory()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"must not replay"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var dispatched = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );
        var runId = dispatched.RunIds.ShouldHaveSingleItem();

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );
        var blocked = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        blocked.Status.ShouldBe(AutomationDispatchStatus.FeatureDisabled);
        var retained = (
            await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None)
        ).ShouldBeOfType<AutomationRunQueryOutcome.Available>();
        retained.Runs.ShouldHaveSingleItem().State.ShouldBe(AutomationFlowRunState.Invalidated);
        await fixture.Features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );
        (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Invalidated
        );
        fixture.Chat.Messages.ShouldBeEmpty();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task SensitiveActionExpression_IsEvaluatedButBlockedBeforePublicOutputAndOutcome()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node(
            "send-chat",
            """{"message":"fallback"}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("message"),
                new(AutomationExpressionLanguage.CurrentVersion, "private_value")
            )
        );
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var dispatched = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, sensitive: "do-not-expose"),
                new CustomCommandSourceConfiguration(new(7))
            ),
            CancellationToken.None
        );

        fixture.Chat.Messages.ShouldBeEmpty();
        var summary = (await fixture.Queries.ListAsync(new(fixture.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldHaveSingleItem();
        summary.State.ShouldBe(AutomationFlowRunState.Failed);
        summary.Nodes.ShouldContain(static node => node.OutcomeCode == "sensitive-output-blocked");
        JsonSerializer.Serialize(summary).ShouldNotContain("do-not-expose");
        (
            await fixture.Runtime.ResumeAsync(
                dispatched.RunIds.ShouldHaveSingleItem(),
                CancellationToken.None
            )
        ).Status.ShouldBe(AutomationResumeStatus.Failed);
    }

    [Test]
    public async Task OverlayCue_ExplicitTargetPersistsAndAdmitsExactlyThatTarget()
    {
        var overlays = new HostBoundOverlayCues();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var target = Guid.NewGuid();
        var otherTargets = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var cue = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, target, OverlayType.CuePlayer);
        foreach (var other in otherTargets)
        {
            overlays.AddTarget(fixture.HostId, other, OverlayType.CuePlayer);
        }
        overlays.AddCue(fixture.HostId, cue, OverlayCueQueuePolicy.Replace);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("target-id"),
                new(AutomationExpressionLanguage.CurrentVersion, "target_id")
            )
        );
        var flowId = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var outcome = await fixture.Runtime.DispatchAsync(
            new(
                Context(fixture.HostId, targetId: target),
                new CustomCommandSourceConfiguration(new(7))
            ),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        overlays.Admissions.ShouldHaveSingleItem().TargetOverlayId.ShouldBe(target);
        overlays.Admissions.ShouldHaveSingleItem().CueId.ShouldBe(cue);
        overlays.Admissions.ShouldNotContain(request =>
            otherTargets.Contains(request.TargetOverlayId)
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        var persisted = await db.AutomationFlowNodes.SingleAsync(node =>
            node.FlowId == flowId.Value && node.DefinitionId == "play-overlay-cue"
        );
        persisted.ConfigurationJson.ShouldContain(target.ToString());
        persisted.ConfigurationJson.ShouldContain(cue.ToString());
    }

    [Test]
    public async Task OverlayCue_UnavailableCrossHostOrWrongTypeTargetsFailClosedBeforeAdmission()
    {
        var overlays = new HostBoundOverlayCues();
        await using var fixture = await RuntimeFixture.CreateAsync(
            overlays: overlays,
            hostFeatures: HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var otherHost = await fixture.SeedHostAsync(
            "other-overlay-host",
            HostFeatureFlags.Automations
                | HostFeatureFlags.CustomCommands
                | HostFeatureFlags.Overlays
        );
        var validTarget = Guid.NewGuid();
        var otherValidTarget = Guid.NewGuid();
        var wrongTypeTarget = Guid.NewGuid();
        var otherHostTarget = Guid.NewGuid();
        var cue = Guid.NewGuid();
        overlays.AddTarget(fixture.HostId, validTarget, OverlayType.CuePlayer);
        overlays.AddTarget(fixture.HostId, otherValidTarget, OverlayType.CuePlayer);
        overlays.AddTarget(fixture.HostId, wrongTypeTarget, OverlayType.Giveaway);
        overlays.AddTarget(otherHost, otherHostTarget, OverlayType.CuePlayer);
        overlays.AddCue(fixture.HostId, cue, OverlayCueQueuePolicy.Replace);

        foreach (var target in new[] { wrongTypeTarget, otherHostTarget })
        {
            var source = Node("custom-command", """{"custom-command-id":7}""");
            var action = Node(
                "play-overlay-cue",
                $$"""{"target-id":"{{target}}","cue-id":"{{cue}}"}"""
            );

            var outcome = await fixture.Flows.SaveAsync(
                Draft(fixture.HostId, [source, action], [Edge(source, "flow", action)]),
                CancellationToken.None
            );

            outcome
                .ShouldBeOfType<AutomationFlowSaveOutcome.Invalid>()
                .Errors.ShouldContain(static error =>
                    error.Code == "overlay-reference-unavailable"
                );
        }

        var runtimeSource = Node("custom-command", """{"custom-command-id":7}""");
        var runtimeAction = Node(
            "play-overlay-cue",
            $$"""{"target-id":"{{validTarget}}","cue-id":"{{cue}}"}""",
            fields: ImmutableDictionary<
                AutomationConfigurationFieldId,
                AutomationExpressionSource
            >.Empty.Add(
                new("target-id"),
                new(AutomationExpressionLanguage.CurrentVersion, "target_id")
            )
        );
        _ = await fixture.SaveAsync(
            [runtimeSource, runtimeAction],
            [Edge(runtimeSource, "flow", runtimeAction)]
        );
        foreach (var target in new[] { wrongTypeTarget, otherHostTarget })
        {
            var dispatched = await fixture.Runtime.DispatchAsync(
                new(
                    Context(fixture.HostId, targetId: target),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                CancellationToken.None
            );
            dispatched.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        }

        overlays.Admissions.ShouldBeEmpty();
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlows.CountAsync()).ShouldBe(1);
        (
            await db.AutomationFlowRuns.CountAsync(static run =>
                run.Status == AutomationFlowRunStatus.Failed
            )
        ).ShouldBe(2);
    }

    [Test]
    public async Task RuntimeAndQueries_AreHostIsolated()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var otherHost = await fixture.SeedHostAsync(
            "other",
            HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands
        );
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node("send-chat", """{"message":"host one"}""");
        _ = await fixture.SaveAsync([source, action], [Edge(source, "flow", action)]);

        var outcome = await fixture.Runtime.DispatchAsync(
            new(Context(otherHost), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.NoMatchingFlow);
        fixture.Chat.Messages.ShouldBeEmpty();
        (await fixture.Queries.ListAsync(new(otherHost), CancellationToken.None))
            .ShouldBeOfType<AutomationRunQueryOutcome.Available>()
            .Runs.ShouldBeEmpty();
    }

    [Test]
    public async Task CustomCommandAdapter_Dispatching_StartsEveryMatchingEnabledSourceOwnedFlow()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var firstSource = Node("custom-command", """{"custom-command-id":7}""");
        var secondSource = Node("custom-command", """{"custom-command-id":7}""");
        var otherSource = Node("custom-command", """{"custom-command-id":8}""");
        var first = await fixture.SaveAsync([firstSource], []);
        var second = await fixture.SaveAsync([secondSource], []);
        var disabled = await fixture.SaveAsync([otherSource], []);
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var flow = await db.AutomationFlows.SingleAsync(value => value.Id == disabled.Value);
            flow.IsEnabled = false;
            _ = await db.SaveChangesAsync();
        }

        var available = await fixture.Runtime.AvailableCustomCommandIdsAsync(
            new(fixture.HostId),
            CancellationToken.None
        );
        available.ShouldBe(new HashSet<int> { 7 });
        var outcome = await fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            CancellationToken.None
        );

        outcome.Status.ShouldBe(AutomationDispatchStatus.Accepted);
        outcome.RunIds.Length.ShouldBe(2);
        await using var verify = await fixture.Database.CreateDbContextAsync();
        var runs = await verify.AutomationFlowRuns.AsNoTracking().ToArrayAsync();
        runs.Length.ShouldBe(2);
        runs.Select(static run => run.FlowId)
            .ToHashSet()
            .SetEquals([first.Value, second.Value])
            .ShouldBeTrue();
        runs.ShouldAllBe(run =>
            run.HostId == fixture.HostId && run.Status == AutomationFlowRunStatus.Completed
        );

        await fixture.Features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.CustomCommands,
            CancellationToken.None
        );
        (
            await fixture.Runtime.AvailableCustomCommandIdsAsync(
                new(fixture.HostId),
                CancellationToken.None
            )
        ).ShouldBeEmpty();
    }

    private static RecordingChatSender InterruptFirstSend(TaskCompletionSource entered) =>
        new(
            null,
            async (call, _, cancellationToken) =>
            {
                if (call != 1)
                {
                    return;
                }

                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        );

    private static async Task<Guid> SingleRunIdAsync(RuntimeFixture fixture)
    {
        await using var db = await fixture.Database.CreateDbContextAsync();
        return await db.AutomationFlowRuns.Select(static run => run.Id).SingleAsync();
    }

    private static AutomationFlowDraft Draft(
        int hostId,
        ImmutableArray<AutomationFlowDraftNode> nodes,
        ImmutableArray<AutomationFlowDraftEdge> edges
    ) => new(null, new(hostId), "Flow", 1, true, nodes, edges);

    private static AutomationFlowDraftNode Node(
        string type,
        string json,
        AutomationNodeFailurePolicy policy = AutomationNodeFailurePolicy.Stop,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource>? fields =
            null,
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>? bindings = null
    )
    {
        using var document = JsonDocument.Parse(json);
        return new(
            new(Guid.NewGuid()),
            new(type, 1, document.RootElement.Clone()),
            AutomationExpressionLanguage.CurrentVersion,
            policy,
            bindings
                ?? fields?.ToImmutableDictionary(
                    static pair => pair.Key,
                    static pair => new AutomationInputBinding(
                        AutomationInputBindingMode.Expression,
                        pair.Value
                    )
                )
                ?? ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty
        );
    }

    private static PersistedAutomationNodeDefinition Persisted(string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(type, 1, document.RootElement.Clone());
    }

    private static string TransformJson(
        ImmutableArray<TransformInput> inputs,
        ImmutableArray<TransformOutput> outputs
    ) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["inputs"] = inputs
                    .Select(static input => new Dictionary<string, object?>
                    {
                        ["port-id"] = input.PortId,
                        ["cel-identifier"] = input.Identifier,
                        ["display-name"] = input.DisplayName,
                        ["binding-field-id"] = input.BindingFieldId,
                        ["type"] = input.ValueType.ToString(),
                        ["nullability"] = input.Nullability.ToString(),
                        ["fixed"] = input.Fixed,
                    })
                    .ToArray(),
                ["outputs"] = outputs
                    .Select(static output => new Dictionary<string, object?>
                    {
                        ["port-id"] = output.PortId,
                        ["display-name"] = output.DisplayName,
                        ["type"] = output.ValueType.ToString(),
                        ["nullability"] = output.Nullability.ToString(),
                        ["cel"] = output.Source,
                    })
                    .ToArray(),
            },
            JsonSerializerOptions.Web
        );

    private static AutomationCelTransformConfiguration TransformConfiguration(
        string first,
        string second
    ) =>
        new(
            [
                new(
                    new("amount-input"),
                    new("amount"),
                    "Amount",
                    new("amount-binding"),
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Number(0m)
                ),
            ],
            [
                new(
                    new("first-output"),
                    "First",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    first
                ),
                new(
                    new("second-output"),
                    "Second",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    second
                ),
            ]
        );

    private static AutomationCelTransformConfiguration TransformDynamicPrecisionConfiguration(
        string first,
        string second
    ) =>
        new(
            [
                new(
                    new("amount-input"),
                    new("amount"),
                    "Amount",
                    new("amount-binding"),
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Number(0m)
                ),
                new(
                    new("precision-input"),
                    new("precision"),
                    "Precision",
                    new("precision-binding"),
                    AutomationPortValueType.Number,
                    AutomationPortNullability.NonNullable,
                    new AutomationValue.Number(2m)
                ),
            ],
            [
                new(
                    new("first-output"),
                    "First",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    first
                ),
                new(
                    new("second-output"),
                    "Second",
                    AutomationPortValueType.Text,
                    AutomationPortNullability.NonNullable,
                    second
                ),
            ]
        );

    private sealed record TransformInput(
        string PortId,
        string Identifier,
        string DisplayName,
        string BindingFieldId,
        AutomationPortValueType ValueType,
        AutomationPortNullability Nullability,
        object? Fixed
    );

    private sealed record TransformOutput(
        string PortId,
        string DisplayName,
        AutomationPortValueType ValueType,
        AutomationPortNullability Nullability,
        string Source
    );

    private static AutomationFlowDraftNode ConnectedNode(string type) =>
        Node(
            type,
            """{"value":1}""",
            bindings: Bindings("value", AutomationInputBindingMode.Connected)
        );

    private static ImmutableDictionary<
        AutomationConfigurationFieldId,
        AutomationInputBinding
    > Bindings(
        string fieldId,
        AutomationInputBindingMode mode,
        AutomationExpressionSource? expression = null
    ) =>
        ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty.Add(
            new(fieldId),
            new(mode, expression)
        );

    private static async Task<ImmutableArray<string>> ValidationCodes(
        RuntimeFixture fixture,
        AutomationFlowDraft draft
    ) =>
        (await fixture.Flows.ValidateDraftAsync(draft, CancellationToken.None))
            .ShouldBeOfType<AutomationFlowValidationOutcome.Invalid>()
            .Errors.Select(static error => error.Code)
            .ToImmutableArray();

    private static async Task AssertValidationCode(
        RuntimeFixture fixture,
        AutomationFlowDraft draft,
        string expected
    ) => (await ValidationCodes(fixture, draft)).ShouldContain(expected);

    private static AutomationFlowDraftEdge Edge(
        AutomationFlowDraftNode source,
        string sourcePort,
        AutomationFlowDraftNode target,
        string targetPort = "flow",
        AutomationEdgeKind kind = AutomationEdgeKind.Flow
    ) => new(Guid.NewGuid(), kind, source.Id, new(sourcePort), target.Id, new(targetPort));

    private static AutomationContext Context(
        int hostId,
        string argument = "yes",
        string sensitive = "private",
        Guid? targetId = null,
        AutomationDefinitionId? sourceDefinitionId = null
    ) =>
        new(
            new(Guid.NewGuid(), sourceDefinitionId ?? AutomationDefinitionIds.CustomCommandSource),
            new("viewer-id", "viewer", "Viewer"),
            new(new(hostId), $"channel-{hostId}", $"host-{hostId}", $"Host {hostId}"),
            null,
            new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            [new(0, argument)],
            new(
                new Dictionary<AutomationVariableName, AutomationVariable>
                {
                    [new("private_value")] = new(
                        new AutomationValue.Text(sensitive),
                        AutomationDataSensitivity.Sensitive
                    ),
                    [new("target_id")] = new(
                        new AutomationValue.Text((targetId ?? Guid.Empty).ToString()),
                        AutomationDataSensitivity.Safe
                    ),
                }
            )
        );

    private sealed record DataContractConfiguration : AutomationConfiguration;

    private sealed class TestPureHandler(
        AutomationPureHandlerContract contract,
        Func<AutomationPureNodeInput, AutomationPureNodeResult> execute
    ) : IAutomationPureNodeHandler
    {
        private int _calls;

        public AutomationPureHandlerContract Contract { get; } = contract;

        internal int Calls => Volatile.Read(ref _calls);

        public ValueTask<AutomationPureNodeResult> ExecuteAsync(
            AutomationPureNodeInput input,
            CancellationToken cancellationToken
        )
        {
            _ = Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(execute(input));
        }
    }

    private sealed class PausingPureHandler(
        IAutomationPureNodeHandler inner,
        TaskCompletionSource<AutomationPureNodeResult.Succeeded> outputsReady,
        Task release
    ) : IAutomationPureNodeHandler
    {
        public AutomationPureHandlerContract Contract => inner.Contract;

        public async ValueTask<AutomationPureNodeResult> ExecuteAsync(
            AutomationPureNodeInput input,
            CancellationToken cancellationToken
        )
        {
            var result = await inner.ExecuteAsync(input, cancellationToken);
            if (result is AutomationPureNodeResult.Succeeded succeeded)
            {
                outputsReady.SetResult(succeeded);
                await release;
            }

            return result;
        }
    }

    private sealed class ActionDataOutputModule : IAutomationCatalogModule
    {
        public AutomationModuleId Id => new("tests.action-output");

        public IEnumerable<IAutomationDefinition> Definitions =>
            [
                new AutomationDefinition<DataContractConfiguration>(
                    new(
                        new("test-action-data-output"),
                        AutomationNodeKind.Action,
                        AutomationDefinitionScope.Host,
                        new(new(1), new(1)),
                        new("Invalid action", "Declares a Data output.", "Test"),
                        [],
                        [
                            new(
                                new("value"),
                                "Value",
                                "An invalid action result.",
                                AutomationPortValueType.Text
                            ),
                        ],
                        [],
                        AutomationActionCapabilities.SendsChat,
                        AutomationActionRetrySafety.Unsafe
                    ),
                    static _ => new AutomationConfigurationParseResult.Parsed(
                        new DataContractConfiguration()
                    ),
                    static _ => AutomationValidationResult.Valid
                ),
            ];
    }

    private sealed class DataContractAutomationModule : IAutomationCatalogModule
    {
        private static readonly AutomationSchemaCompatibility _schema = new(new(1), new(1));
        private static readonly AutomationPortMetadata _flowInput = new(
            new("flow"),
            "Flow",
            "Runs this node.",
            AutomationPortValueType.Flow
        );
        private static readonly AutomationPortMetadata _completeOutput = new(
            new("complete"),
            "Complete",
            "Continues after this node.",
            AutomationPortValueType.Flow
        );

        public AutomationModuleId Id => new("tests.data-contract");

        public IEnumerable<IAutomationDefinition> Definitions =>
            [
                Definition(
                    "test-number-source",
                    AutomationNodeKind.Source,
                    [],
                    [
                        new(new("flow"), "Flow", "Starts the flow.", AutomationPortValueType.Flow),
                        DataOutput(AutomationPortValueType.Number),
                    ],
                    []
                ),
                AutomationCelTransform.Definition(
                    new("test-cel-transform"),
                    new(
                        "CEL Transform test seam",
                        "Exercises an authored Transform without product registration.",
                        "Test"
                    )
                ),
                Definition(
                    "test-number-value",
                    AutomationNodeKind.Value,
                    [],
                    [DataOutput(AutomationPortValueType.Number)],
                    []
                ),
                Definition(
                    "test-text-value",
                    AutomationNodeKind.Value,
                    [],
                    [DataOutput(AutomationPortValueType.Text)],
                    []
                ),
                Definition(
                    "test-nullable-number-value",
                    AutomationNodeKind.Value,
                    [],
                    [
                        DataOutput(
                            AutomationPortValueType.Number,
                            nullability: AutomationPortNullability.Nullable
                        ),
                    ],
                    []
                ),
                Definition(
                    "test-sensitive-number-value",
                    AutomationNodeKind.Value,
                    [],
                    [
                        DataOutput(
                            AutomationPortValueType.Number,
                            sensitivity: AutomationDataSensitivity.Sensitive
                        ),
                    ],
                    []
                ),
                Definition(
                    "test-stream-value",
                    AutomationNodeKind.Value,
                    [],
                    [DataOutput(AutomationPortValueType.Stream)],
                    []
                ),
                Definition(
                    "test-number-transform",
                    AutomationNodeKind.Transform,
                    [DataInput(required: true)],
                    [DataOutput(AutomationPortValueType.Number)],
                    [Field(required: true)]
                ),
                Consumer("test-number-consumer", required: true),
                Consumer("test-nullable-number-consumer", required: false),
                Definition(
                    "test-counting-text-value",
                    AutomationNodeKind.Value,
                    [],
                    [DataOutput(AutomationPortValueType.Text)],
                    []
                ),
                Definition(
                    "test-failing-text-value",
                    AutomationNodeKind.Value,
                    [],
                    [DataOutput(AutomationPortValueType.Text)],
                    []
                ),
                Definition(
                    "test-text-transform",
                    AutomationNodeKind.Transform,
                    [DataInput("input", AutomationPortValueType.Text, required: true)],
                    [DataOutput(AutomationPortValueType.Text)],
                    [Field("input", AutomationPortValueType.Text, required: true)]
                ),
                Definition(
                    "test-display-name-transform",
                    AutomationNodeKind.Transform,
                    [DataInput("actor", AutomationPortValueType.Actor, required: true)],
                    [DataOutput(AutomationPortValueType.Text)],
                    [Field("actor", AutomationPortValueType.Actor, required: true)]
                ),
                TextConsumer(),
                DataDelay(),
                Definition(
                    "test-data-control",
                    AutomationNodeKind.Control,
                    [_flowInput, DataInput(required: true)],
                    [_completeOutput],
                    [Field(required: true)]
                ),
                Definition(
                    "test-data-control-output",
                    AutomationNodeKind.Control,
                    [_flowInput],
                    [_completeOutput, DataOutput(AutomationPortValueType.Number)],
                    []
                ),
            ];

        private static IAutomationDefinition Consumer(string id, bool required) =>
            Definition(
                id,
                AutomationNodeKind.Action,
                [_flowInput, DataInput(required)],
                [_completeOutput],
                [Field(required)]
            );

        private static IAutomationDefinition TextConsumer() =>
            new AutomationDefinition<SendChatActionConfiguration>(
                new(
                    new("test-text-consumer"),
                    AutomationNodeKind.Action,
                    AutomationDefinitionScope.Host,
                    _schema,
                    new("Text consumer", "Sends the resolved test text.", "Test"),
                    [
                        _flowInput,
                        DataInput("message", AutomationPortValueType.Text, required: true),
                    ],
                    [_completeOutput],
                    [
                        new(
                            new("message"),
                            "Message",
                            "The resolved message.",
                            new AutomationConfigurationFieldType.Text(500),
                            true
                        ),
                    ],
                    AutomationActionCapabilities.SendsChat,
                    AutomationActionRetrySafety.Unsafe
                ),
                static json =>
                    json.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                        ? new AutomationConfigurationParseResult.Parsed(
                            new SendChatActionConfiguration(message.GetString()!)
                        )
                        : new AutomationConfigurationParseResult.Invalid([]),
                static configuration =>
                    string.IsNullOrWhiteSpace(configuration.Message)
                        ? AutomationValidationResult.Invalid(
                            new AutomationValidationTarget.Field(new("message")),
                            "Enter a fallback message."
                        )
                        : AutomationValidationResult.Valid
            );

        private static IAutomationDefinition DataDelay() =>
            new AutomationDefinition<DelayControlConfiguration>(
                new(
                    new("test-data-delay"),
                    AutomationNodeKind.Control,
                    AutomationDefinitionScope.Host,
                    _schema,
                    new("Data delay", "Waits after resolving test data.", "Test"),
                    [_flowInput, DataInput("value", AutomationPortValueType.Text, required: true)],
                    [_completeOutput],
                    [
                        new(
                            new("duration-milliseconds"),
                            "Duration",
                            "The delay duration.",
                            new AutomationConfigurationFieldType.Duration(
                                TimeSpan.FromMilliseconds(1),
                                null
                            ),
                            true
                        ),
                        Field("value", AutomationPortValueType.Text, required: true),
                    ],
                    AutomationActionCapabilities.None,
                    AutomationActionRetrySafety.NotApplicable
                ),
                static json =>
                    json.TryGetProperty("duration-milliseconds", out var duration)
                    && duration.TryGetInt64(out var milliseconds)
                    && milliseconds > 0
                        ? new AutomationConfigurationParseResult.Parsed(
                            new DelayControlConfiguration(TimeSpan.FromMilliseconds(milliseconds))
                        )
                        : new AutomationConfigurationParseResult.Invalid([]),
                static configuration =>
                    configuration.Duration > TimeSpan.Zero
                        ? AutomationValidationResult.Valid
                        : AutomationValidationResult.Invalid(
                            new AutomationValidationTarget.Field(new("duration-milliseconds")),
                            "Choose a positive delay."
                        )
            );

        private static IAutomationDefinition Definition(
            string id,
            AutomationNodeKind kind,
            ImmutableArray<AutomationPortMetadata> inputs,
            ImmutableArray<AutomationPortMetadata> outputs,
            ImmutableArray<AutomationConfigurationFieldMetadata> fields
        ) =>
            new AutomationDefinition<DataContractConfiguration>(
                new(
                    new(id),
                    kind,
                    AutomationDefinitionScope.Host,
                    _schema,
                    new(id, "Exercises the typed graph contract.", "Test"),
                    inputs,
                    outputs,
                    fields,
                    AutomationActionCapabilities.None,
                    kind == AutomationNodeKind.Action
                        ? AutomationActionRetrySafety.Unsafe
                        : AutomationActionRetrySafety.NotApplicable
                ),
                static _ => new AutomationConfigurationParseResult.Parsed(
                    new DataContractConfiguration()
                ),
                static _ => AutomationValidationResult.Valid
            );

        private static AutomationPortMetadata DataInput(bool required) =>
            DataInput("value", AutomationPortValueType.Number, required);

        private static AutomationPortMetadata DataInput(
            string id,
            AutomationPortValueType valueType,
            bool required
        ) =>
            new(
                new(id),
                "Value",
                "Receives the exact typed value.",
                valueType,
                Nullability: required
                    ? AutomationPortNullability.NonNullable
                    : AutomationPortNullability.Nullable,
                BindingFieldId: new(id)
            );

        private static AutomationPortMetadata DataOutput(
            AutomationPortValueType type,
            AutomationDataSensitivity sensitivity = AutomationDataSensitivity.Safe,
            AutomationPortNullability nullability = AutomationPortNullability.NonNullable
        ) => new(new("value"), "Value", "Supplies a typed value.", type, sensitivity, nullability);

        private static AutomationConfigurationFieldMetadata Field(bool required) =>
            Field("value", AutomationPortValueType.Number, required);

        private static AutomationConfigurationFieldMetadata Field(
            string id,
            AutomationPortValueType valueType,
            bool required
        ) =>
            new(
                new(id),
                "Value",
                "The retained Fixed value.",
                valueType == AutomationPortValueType.Number
                    ? new AutomationConfigurationFieldType.Number(0, null)
                    : new AutomationConfigurationFieldType.Data(valueType),
                required
            );
    }

    private static TestPureHandler TextValueHandler(string definitionId, string value) =>
        new(
            new(
                new(definitionId),
                AutomationNodeKind.Value,
                [],
                [
                    new(
                        new("value"),
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable
                    ),
                ]
            ),
            _ => new AutomationPureNodeResult.Succeeded(
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                    new("value"),
                    new(new AutomationValue.Text(value), [AutomationValueProvenance.Generated])
                )
            )
        );

    private static TestPureHandler CompositeValueHandler(
        string definitionId,
        AutomationPortValueType valueType,
        AutomationValue value
    ) =>
        new(
            new(
                new(definitionId),
                AutomationNodeKind.Value,
                [],
                [new(new("value"), valueType, AutomationPortNullability.NonNullable)]
            ),
            _ => new AutomationPureNodeResult.Succeeded(
                ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                    new("value"),
                    new(value, [AutomationValueProvenance.Generated])
                )
            )
        );

    private static TestPureHandler FailingTextValueHandler() =>
        new(
            new(
                new("test-failing-text-value"),
                AutomationNodeKind.Value,
                [],
                [
                    new(
                        new("value"),
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable
                    ),
                ]
            ),
            static _ => new AutomationPureNodeResult.Failed("deterministic-failure")
        );

    private static TestPureHandler TextTransformHandler() =>
        new(
            new(
                new("test-text-transform"),
                AutomationNodeKind.Transform,
                [
                    new(
                        new("input"),
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable
                    ),
                ],
                [
                    new(
                        new("value"),
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable
                    ),
                ]
            ),
            static input =>
            {
                var value = input.Inputs[new("input")].Value.ShouldBeOfType<AutomationValue.Text>();
                return new AutomationPureNodeResult.Succeeded(
                    ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                        new("value"),
                        new(
                            new AutomationValue.Text($"{value.Value}-transformed"),
                            input
                                .Inputs[new("input")]
                                .Provenance.Append(AutomationValueProvenance.Generated)
                                .Distinct()
                                .Order()
                                .ToImmutableArray()
                        )
                    )
                );
            }
        );

    private static TestPureHandler DisplayNameHandler() =>
        new(
            new(
                new("test-display-name-transform"),
                AutomationNodeKind.Transform,
                [
                    new(
                        new("actor"),
                        AutomationPortValueType.Actor,
                        AutomationPortNullability.NonNullable
                    ),
                ],
                [
                    new(
                        new("value"),
                        AutomationPortValueType.Text,
                        AutomationPortNullability.NonNullable
                    ),
                ]
            ),
            static input =>
            {
                var actor = input
                    .Inputs[new("actor")]
                    .Value.ShouldBeOfType<AutomationValue.Actor>();
                return new AutomationPureNodeResult.Succeeded(
                    ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                        new("value"),
                        new(
                            new AutomationValue.Text(actor.Value.DisplayName),
                            input
                                .Inputs[new("actor")]
                                .Provenance.Append(AutomationValueProvenance.Generated)
                                .Distinct()
                                .Order()
                                .ToImmutableArray()
                        )
                    )
                );
            }
        );

    private sealed class RuntimeFixture : IAsyncDisposable
    {
        private RuntimeFixture(
            SqliteBlokeBotDbFactory database,
            MutableTimeProvider clock,
            RecordingChatSender chat,
            HostFeatureService features,
            AutomationCatalogService catalog,
            AutomationCelTransformHandler transformHandler,
            AutomationExpressionService expressions,
            AutomationActionExecutor actions,
            AutomationRuntimeService runtime,
            AutomationFlowService flows,
            AutomationRunQueryService queries,
            int hostId
        )
        {
            Database = database;
            Clock = clock;
            Chat = chat;
            Features = features;
            Catalog = catalog;
            TransformHandler = transformHandler;
            Expressions = expressions;
            Actions = actions;
            Runtime = runtime;
            Flows = flows;
            Queries = queries;
            HostId = hostId;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal MutableTimeProvider Clock { get; }
        internal RecordingChatSender Chat { get; }
        internal HostFeatureService Features { get; }
        internal AutomationCatalogService Catalog { get; }
        internal AutomationCelTransformHandler TransformHandler { get; }
        internal AutomationExpressionService Expressions { get; }
        internal AutomationActionExecutor Actions { get; }
        internal AutomationRuntimeService Runtime { get; }
        internal AutomationFlowService Flows { get; }
        internal AutomationRunQueryService Queries { get; }
        internal int HostId { get; }

        internal static async Task<RuntimeFixture> CreateAsync(
            IEnumerable<bool>? chatAdmissions = null,
            IOverlayCueAdmissionService? overlays = null,
            HostFeatureFlags hostFeatures =
                HostFeatureFlags.Automations | HostFeatureFlags.CustomCommands,
            RecordingChatSender? chat = null,
            IEnumerable<IAutomationPureNodeHandler>? handlers = null,
            Func<AutomationCelTransformHandler, IAutomationPureNodeHandler>? transformDecorator =
                null
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var clock = new MutableTimeProvider(
                new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
            );
            chat ??= new RecordingChatSender(chatAdmissions);
            var observer = new AutomationFeatureDisableObserver(database, clock);
            var features = new HostFeatureService(
                database,
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                [],
                [observer]
            );
            var expressions = new AutomationExpressionService();
            var transformHandler = new AutomationCelTransformHandler(new("test-cel-transform"));
            var registeredTransformHandler =
                transformDecorator?.Invoke(transformHandler) ?? transformHandler;
            var catalog = new AutomationCatalogService(
                new([new CoreAutomationCatalogModule(), new DataContractAutomationModule()]),
                features,
                expressions,
                (handlers ?? []).Append(registeredTransformHandler)
            );
            overlays ??= new NoOverlayCues();
            var actions = new AutomationActionExecutor(features, chat, overlays, expressions);
            var flows = new AutomationFlowService(database, catalog, expressions, overlays, clock);
            var runtime = new AutomationRuntimeService(
                database,
                catalog,
                flows,
                expressions,
                actions,
                clock
            );
            var queries = new AutomationRunQueryService(database, features, catalog);
            var fixture = new RuntimeFixture(
                database,
                clock,
                chat,
                features,
                catalog,
                transformHandler,
                expressions,
                actions,
                runtime,
                flows,
                queries,
                0
            );
            var hostId = await fixture.SeedHostAsync("streamer", hostFeatures);
            await fixture.SeedAutomationCommandsAsync(hostId);
            return new RuntimeFixture(
                database,
                clock,
                chat,
                features,
                catalog,
                transformHandler,
                expressions,
                actions,
                runtime,
                flows,
                queries,
                hostId
            );
        }

        internal AutomationRuntimeService NewRuntime() =>
            new(Database, Catalog, Flows, Expressions, Actions, Clock);

        internal async Task<AutomationFlowId> SaveAsync(
            ImmutableArray<AutomationFlowDraftNode> nodes,
            ImmutableArray<AutomationFlowDraftEdge> edges
        ) =>
            (await Flows.SaveAsync(Draft(HostId, nodes, edges), CancellationToken.None))
                .ShouldBeOfType<AutomationFlowSaveOutcome.Saved>()
                .FlowId;

        internal async Task<int> SeedHostAsync(string login, HostFeatureFlags enabledFeatures)
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = $"{login}-id",
                Login = login,
                DisplayName = login,
                EnabledFeatures = enabledFeatures,
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            return host.Id;
        }

        private async Task SeedAutomationCommandsAsync(int hostId)
        {
            await using var db = await Database.CreateDbContextAsync();
            db.CustomCommands.AddRange(
                new CustomCommand
                {
                    Id = 7,
                    HostId = hostId,
                    Name = "automation-seven",
                    CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                    UpdatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                    Action = new AutomationCustomCommandAction { HostId = hostId },
                },
                new CustomCommand
                {
                    Id = 8,
                    HostId = hostId,
                    Name = "automation-eight",
                    CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                    UpdatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                    Action = new AutomationCustomCommandAction { HostId = hostId },
                }
            );
            _ = await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();
    }

    private sealed class RecordingChatSender(
        IEnumerable<bool>? admissions,
        Func<int, string, CancellationToken, ValueTask>? beforeSend = null
    ) : IPublicChatMessageSender
    {
        private readonly Queue<bool> _admissions = new(admissions ?? []);
        private int _calls;

        internal ConcurrentQueue<string> Messages { get; } = [];

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Messages.Enqueue(message);
            var call = Interlocked.Increment(ref _calls);
            if (beforeSend is not null)
            {
                await beforeSend(call, message, cancellationToken);
            }

            return _admissions.TryDequeue(out var accepted) && !accepted
                ? new PublicChatSendOutcome.Rejected()
                : new PublicChatSendOutcome.Accepted();
        }
    }

    private sealed class NoOverlayCues : IOverlayCueAdmissionService
    {
        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<OverlayCueReferenceOutcome>(
                new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue)
            );

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }

    private sealed class HostBoundOverlayCues : IOverlayCueAdmissionService
    {
        private readonly List<OverlayTargetFixture> _targets = [];
        private readonly List<OverlayCueFixture> _cues = [];

        internal List<OverlayCueAdmissionRequest> Admissions { get; } = [];

        internal void AddTarget(int hostId, Guid id, OverlayType type) =>
            _targets.Add(new(hostId, id, type));

        internal void AddCue(int hostId, Guid id, OverlayCueQueuePolicy queuePolicy) =>
            _cues.Add(new(hostId, id, queuePolicy));

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult(ResolveReferences(request));

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new OverlayCueAdmissionCatalog(
                    _targets
                        .Where(target =>
                            target.HostId == hostId && target.Type == OverlayType.CuePlayer
                        )
                        .Select(static target => new OverlayCueTargetChoice(
                            target.Id,
                            target.Id.ToString()
                        ))
                        .ToImmutableArray(),
                    _cues
                        .Where(cue => cue.HostId == hostId)
                        .Select(static cue => new OverlayCueChoice(
                            cue.Id,
                            cue.Id.ToString(),
                            cue.QueuePolicy
                        ))
                        .ToImmutableArray()
                )
            );

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                ResolveReferences(new(request.HostId, request.TargetOverlayId, request.CueId))
                is OverlayCueReferenceOutcome.Available
                    ? Record(request)
                    : new OverlayCueAdmissionOutcome.Missing()
            );

        private OverlayCueReferenceOutcome ResolveReferences(OverlayCueReferenceRequest request)
        {
            var target = _targets.SingleOrDefault(candidate =>
                candidate.HostId == request.HostId && candidate.Id == request.TargetOverlayId
            );
            return target switch
            {
                null or { Type: not OverlayType.CuePlayer } =>
                    new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Target),
                _ when _cues.Any(candidate =>
                        candidate.HostId == request.HostId && candidate.Id == request.CueId
                    ) => new OverlayCueReferenceOutcome.Available(),
                _ => new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Cue),
            };
        }

        private OverlayCueAdmissionOutcome Record(OverlayCueAdmissionRequest request)
        {
            Admissions.Add(request);
            return new OverlayCueAdmissionOutcome.Running(Guid.NewGuid());
        }

        private sealed record OverlayTargetFixture(int HostId, Guid Id, OverlayType Type);

        private sealed record OverlayCueFixture(
            int HostId,
            Guid Id,
            OverlayCueQueuePolicy QueuePolicy
        );
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan duration) => now += duration;
    }
}
