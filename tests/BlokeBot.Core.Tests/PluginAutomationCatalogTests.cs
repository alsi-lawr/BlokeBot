using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginAutomationCatalogTests
{
    [Test]
    public void DegradedFeature_PublishesHotCatalogAndSynthesizesOnlyFlowEdges()
    {
        var registry = new PluginAutomationCatalogRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(automations: registry);
        var snapshots = new PluginFeatureSnapshotRegistry(automations: registry);
        var manifest = ValidatedManifest();
        var fence = Fence();
        declarations.Publish(manifest, fence);
        var declaration = declarations.Current.Declarations[manifest.Manifest.Id];
        var state = State(declaration, fence, revision: 1, enabled: true);
        snapshots.Publish(state);
        var feature = declaration.FindFeature(Feature("publishing"))!;

        var prepared = registry
            .Prepare(declaration, feature, state, Guid.NewGuid())
            .ShouldBeOfType<PluginAutomationPlanOutcome.Prepared>();
        var template = prepared.Plan.Templates.ShouldHaveSingleItem();
        var source = template.Nodes.Single(node =>
            node.DefinitionId.EndsWith(".queued-link", StringComparison.Ordinal)
        );
        var action = template.Nodes.Single(node =>
            node.DefinitionId.EndsWith(".publish-link", StringComparison.Ordinal)
        );
        var flow = template.Edges.Single(edge => edge.Kind == PluginAutomationStoreEdgeKind.Flow);
        var data = template.Edges.Single(edge => edge.Kind == PluginAutomationStoreEdgeKind.Data);

        flow.SourceNodeId.ShouldBe(source.Id);
        flow.SourcePortId.ShouldBe("next");
        flow.TargetNodeId.ShouldBe(action.Id);
        flow.TargetPortId.ShouldBe("flow");
        data.SourcePortId.ShouldBe("link");
        data.TargetPortId.ShouldBe("link");
        AutomationRuntimeSerialization
            .RestoreInputBindings(action.InputBindingsJson)
            .ShouldBeOfType<AutomationInputBindingsRestoreOutcome.Available>()
            .Bindings.Single()
            .Value.Mode.ShouldBe(AutomationInputBindingMode.Connected);

        var catalog = new AutomationDefinitionCatalog([], registry);
        var staleDefinitionId = PluginAutomationCatalogRegistry.DefinitionId(
            manifest.Manifest.Id,
            Definition("publish-link")
        );
        catalog.TryResolve(staleDefinitionId, out _).ShouldBeTrue();
        catalog.IsFormat1Definition(staleDefinitionId).ShouldBeFalse();
        var secondHostState = State(declaration, fence, revision: 1, enabled: true, hostId: 2);
        snapshots.Publish(secondHostState);
        catalog.TryResolve(new(2), staleDefinitionId, out var secondHostDefinition).ShouldBeTrue();
        secondHostDefinition
            .ShouldBeAssignableTo<IPluginAutomationDefinition>()
            .Endpoint.State.Key.HostId.Value.ShouldBe(2);
        var revision = catalog.Revision;

        snapshots.Publish(State(declaration, fence, revision: 2, enabled: false));

        catalog.Revision.ShouldBeGreaterThan(revision);
        catalog.TryResolve(new(1), staleDefinitionId, out _).ShouldBeFalse();
        catalog.TryResolve(new(2), staleDefinitionId, out _).ShouldBeTrue();
    }

    [Test]
    public void StructuredValues_RoundTripNestedArrayMapAndNilSentinel()
    {
        PluginValue source = new PluginValue.Map([
            new(
                "items",
                new PluginValue.Array([
                    new PluginValue.Number(3.5),
                    new PluginValue.Map([new("missing", new PluginValue.Nil())]),
                ])
            ),
        ]);

        AutomationStructuredValue.TryConvert(source, out var converted).ShouldBeTrue();
        var json = AutomationStructuredValue.Serialize(converted);
        using var document = JsonDocument.Parse(json);
        AutomationStructuredValue.TryRead(document.RootElement, out var restored).ShouldBeTrue();

        AutomationStructuredValue.Serialize(restored).ShouldBe(json);
        var plugin = AutomationStructuredValue
            .ToPluginValue(restored)
            .ShouldBeOfType<PluginValue.Map>();
        plugin.Properties.Single().Name.ShouldBe("items");
        _ = plugin
            .Properties.Single()
            .Value.ShouldBeOfType<PluginValue.Array>()
            .Items[1]
            .ShouldBeOfType<PluginValue.Map>()
            .Properties.Single()
            .Value.ShouldBeOfType<PluginValue.Nil>();
        json.ShouldBe("{\"items\":[3.5,{\"missing\":null}]}");
    }

    [Test]
    public void TemplatePlan_RejectsDuplicateNullableAndIncompatibleDataConnections()
    {
        var manifest = ValidatedManifest().Manifest;
        var template = manifest.AutomationTemplates.ShouldHaveSingleItem();
        var source = manifest.AutomationDefinitions.Single(definition =>
            definition.Id == Definition("queued-link")
        );
        var action = manifest.AutomationDefinitions.Single(definition =>
            definition.Id == Definition("publish-link")
        );
        var value = source with
        {
            Id = Definition("constant-link"),
            Kind = PluginAutomationDefinitionKind.Value,
            Name = "Constant link",
        };
        var valueNode = new PluginAutomationTemplateNode(
            Node("constant"),
            value.Id,
            EmptyConfiguration()
        );
        var duplicate = manifest with
        {
            AutomationDefinitions = manifest.AutomationDefinitions.Add(value),
            AutomationTemplates =
            [
                template with
                {
                    Nodes = template.Nodes.Add(valueNode),
                    Edges = template.Edges.Add(
                        new(Node("constant"), Field("link"), Node("publish"), Field("link"))
                    ),
                },
            ],
        };
        var nullable = manifest with
        {
            AutomationDefinitions =
            [
                source with
                {
                    Outputs = [source.Outputs.ShouldHaveSingleItem() with { Required = false }],
                },
                action,
            ],
        };
        var incompatible = manifest with
        {
            AutomationDefinitions =
            [
                source,
                action with
                {
                    Inputs =
                    [
                        action.Inputs.ShouldHaveSingleItem() with
                        {
                            ValueKind = PluginValueKind.Number,
                        },
                    ],
                },
            ],
        };

        Rejects(duplicate, "more than one Data output");
        Rejects(nullable, "optional output");
        Rejects(incompatible, "incompatible Data connection");
    }

    [Test]
    public void TemplatePlan_RejectsMissingCrossFeatureAndCyclicDependencies()
    {
        var manifest = ValidatedManifest().Manifest;
        var template = manifest.AutomationTemplates.ShouldHaveSingleItem();
        var source = manifest.AutomationDefinitions.Single(definition =>
            definition.Id == Definition("queued-link")
        );
        var action = manifest.AutomationDefinitions.Single(definition =>
            definition.Id == Definition("publish-link")
        );
        var missing = manifest with { AutomationTemplates = [template with { Edges = [] }] };
        var crossFeature = manifest with
        {
            AutomationDefinitions = [source with { FeatureId = Feature("collection") }, action],
        };
        var transformA = action with
        {
            Id = Definition("transform-a"),
            Kind = PluginAutomationDefinitionKind.Transform,
            Name = "Transform A",
            Outputs = source.Outputs,
        };
        var transformB = transformA with { Id = Definition("transform-b"), Name = "Transform B" };
        var cyclic = manifest with
        {
            AutomationDefinitions = manifest
                .AutomationDefinitions.Concat([transformA, transformB])
                .ToImmutableArray(),
            AutomationTemplates =
            [
                template with
                {
                    Nodes = template
                        .Nodes.Concat([
                            new(Node("transform-a"), transformA.Id, EmptyConfiguration()),
                            new(Node("transform-b"), transformB.Id, EmptyConfiguration()),
                        ])
                        .ToImmutableArray(),
                    Edges = template
                        .Edges.Concat([
                            new(
                                Node("transform-a"),
                                Field("link"),
                                Node("transform-b"),
                                Field("link")
                            ),
                            new(
                                Node("transform-b"),
                                Field("link"),
                                Node("transform-a"),
                                Field("link")
                            ),
                        ])
                        .ToImmutableArray(),
                },
            ],
        };

        Rejects(missing, "required input");
        Rejects(crossFeature, "invalid or cross-feature definition");
        Rejects(cyclic, "dependency cycle");
    }

    [Test]
    public void TemplatePlan_RejectsNilForRequiredInputWithoutThrowing()
    {
        var manifest = ValidatedManifest().Manifest;
        var template = manifest.AutomationTemplates.ShouldHaveSingleItem();
        var nilConfiguration = new PluginValue.Map([new("link", new PluginValue.Nil())]);
        var modified = manifest with
        {
            AutomationTemplates =
            [
                template with
                {
                    Nodes = template
                        .Nodes.Select(node =>
                            node.Id == Node("publish")
                                ? node with
                                {
                                    Configuration = nilConfiguration,
                                }
                                : node
                        )
                        .ToImmutableArray(),
                    Edges = [],
                },
            ],
        };

        Rejects(modified, "invalid node configuration");
    }

    private static ValidatedPluginManifest ValidatedManifest() =>
        PluginManifestJson.Validate(
            PluginContractFixtures.CompleteManifestJson(),
            PluginContractFixtures.CompatibleHost()
        )
            is PluginManifestValidationOutcome.Accepted accepted
            ? accepted.Manifest
            : throw new InvalidOperationException("The plugin fixture is invalid.");

    private static PluginAutomationPlanOutcome Plan(PluginManifest manifest)
    {
        var fence = Fence();
        var declaration = new PluginFeatureDeclaration(
            new(manifest.Id, manifest.Release),
            fence,
            manifest
        );
        var state = State(declaration, fence, revision: 1, enabled: true);
        return new PluginAutomationCatalogRegistry().Prepare(
            declaration,
            declaration.FindFeature(Feature("publishing"))!,
            state,
            Guid.NewGuid()
        );
    }

    private static void Rejects(PluginManifest manifest, string expectedDiagnostic)
    {
        var rejected = Plan(manifest).ShouldBeOfType<PluginAutomationPlanOutcome.Rejected>();
        rejected.Diagnostic.ShouldContain(expectedDiagnostic);
    }

    private static PluginValue.Map EmptyConfiguration() => new([]);

    private static PluginFeatureState State(
        PluginFeatureDeclaration declaration,
        PluginLifecycleFence fence,
        ulong revision,
        bool enabled,
        int hostId = 1
    )
    {
        PluginHostId.TryCreate(hostId, out var host).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(revision, out var generation).ShouldBeTrue();
        PluginFeatureRevision
            .TryCreate(checked((long)revision), out var featureRevision)
            .ShouldBeTrue();
        PluginReadinessReason
            .TryCreate(
                PluginReadinessReasonCode.MissingScopes,
                PluginRecoveryAction.ReconnectTwitch,
                "Reconnect Twitch.",
                out var reason
            )
            .ShouldBeTrue();
        return new(
            new(declaration.Installation.PluginId, Feature("publishing"), host),
            fence,
            generation,
            enabled
                ? new PluginFeatureReadiness.EnabledDegraded(reason)
                : new PluginFeatureReadiness.Disabled(),
            featureRevision
        );
    }

    private static PluginLifecycleFence Fence()
    {
        PluginWorkerGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        return new(PluginLifecycleOperationId.New(), generation);
    }

    private static PluginFeatureId Feature(string value) =>
        PluginFeatureId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid feature test ID.");

    private static PluginAutomationDefinitionId Definition(string value) =>
        PluginAutomationDefinitionId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid definition test ID.");

    private static PluginAutomationFieldId Field(string value) =>
        PluginAutomationFieldId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid field test ID.");

    private static PluginTemplateNodeId Node(string value) =>
        PluginTemplateNodeId.TryCreate(value, out var id)
            ? id
            : throw new InvalidOperationException("Invalid node test ID.");
}
