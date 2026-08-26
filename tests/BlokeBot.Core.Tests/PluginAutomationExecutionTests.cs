using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Contracts.Testing;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PluginAutomationExecutionTests
{
    [Test]
    [Arguments(PluginAutomationDefinitionKind.Value, "nil-value")]
    [Arguments(PluginAutomationDefinitionKind.Transform, "nil-transform")]
    public async Task RequiredNilPureOutput_ReturnsTypedInvalidPluginOutput(
        PluginAutomationDefinitionKind kind,
        string definitionName
    )
    {
        var fixture = Fixture(new PluginValue.Map([new("link", new PluginValue.Nil())]));

        var result = await fixture.Execution.ExecutePureAsync(
            new(1),
            DefinitionId(fixture.PluginId, definitionName),
            EmptyConfiguration(),
            kind == PluginAutomationDefinitionKind.Transform
                ? ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
                    new("input"),
                    new(new AutomationValue.Text("input"), [AutomationValueProvenance.Generated])
                )
                : ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty,
            CancellationToken.None
        );

        result
            .ShouldBeOfType<AutomationPureNodeResult.Failed>()
            .Code.ShouldBe("plugin-output-invalid");
    }

    [Test]
    [Arguments("publish-link")]
    [Arguments("nil-control")]
    public async Task OutputlessActionAndControl_NilReturnSucceedsWithoutOutputClassification(
        string definitionName
    )
    {
        var fixture = Fixture(new PluginValue.Nil());

        var result = await fixture.Execution.ExecuteActionAsync(
            new(1),
            DefinitionId(fixture.PluginId, definitionName),
            EmptyConfiguration(),
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationResolvedValue>.Empty,
            Context(),
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<AutomationActionOutcome.Succeeded>();
    }

    private static ExecutionFixture Fixture(PluginValue returned)
    {
        var manifest = Manifest();
        var fence = Fence();
        var automations = new PluginAutomationCatalogRegistry();
        var declarations = new PluginFeatureDeclarationRegistry(automations: automations);
        var features = new PluginFeatureSnapshotRegistry(automations: automations);
        declarations.Publish(manifest, fence);
        features.Publish(State(manifest, fence));
        var catalog = new AutomationDefinitionCatalog([], automations);
        return new(new(catalog, new ReturningInvoker(returned)), manifest.Manifest.Id);
    }

    private static ValidatedPluginManifest Manifest()
    {
        var accepted = PluginManifestJson.Validate(
            PluginContractFixtures.CompleteManifestJson(),
            PluginContractFixtures.CompatibleHost()
        );
        var manifest = ((PluginManifestValidationOutcome.Accepted)accepted).Manifest.Manifest;
        var source = manifest.AutomationDefinitions.Single(definition =>
            definition.Id == Definition("queued-link")
        );
        var action = manifest.AutomationDefinitions.Single(definition =>
            definition.Id == Definition("publish-link")
        );
        var definitions = manifest.AutomationDefinitions.AddRange(
            source with
            {
                Id = Definition("nil-value"),
                Kind = PluginAutomationDefinitionKind.Value,
                Name = "Nil value",
            },
            action with
            {
                Id = Definition("nil-transform"),
                Kind = PluginAutomationDefinitionKind.Transform,
                Name = "Nil transform",
                Inputs = [action.Inputs.ShouldHaveSingleItem() with { Id = Field("input") }],
                Outputs = source.Outputs,
            },
            action with
            {
                Id = Definition("nil-control"),
                Kind = PluginAutomationDefinitionKind.Control,
                Name = "Nil control",
            }
        );
        return (
            (PluginManifestValidationOutcome.Accepted)
                PluginManifestValidator.Validate(
                    manifest with
                    {
                        AutomationDefinitions = definitions,
                    },
                    PluginContractFixtures.CompatibleHost()
                )
        ).Manifest;
    }

    private static PluginFeatureState State(
        ValidatedPluginManifest manifest,
        PluginLifecycleFence fence
    )
    {
        PluginHostId.TryCreate(1, out var host).ShouldBeTrue();
        PluginFeatureGeneration.TryCreate(1, out var generation).ShouldBeTrue();
        PluginFeatureRevision.TryCreate(1, out var revision).ShouldBeTrue();
        return new(
            new(manifest.Manifest.Id, Feature("publishing"), host),
            fence,
            generation,
            new PluginFeatureReadiness.Ready(),
            revision
        );
    }

    private static AutomationContext Context() =>
        new(
            new(Guid.NewGuid(), new("plugin.test.source")),
            null,
            new(new(1), "streamer", "Streamer", "streamer"),
            null,
            new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            [],
            new AutomationVariableSet([])
        );

    private static PluginAutomationConfiguration EmptyConfiguration() => new([]);

    private static AutomationDefinitionId DefinitionId(PluginId pluginId, string definition) =>
        PluginAutomationCatalogRegistry.DefinitionId(pluginId, Definition(definition));

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

    private sealed record ExecutionFixture(
        PluginAutomationExecutionService Execution,
        PluginId PluginId
    );

    private sealed class ReturningInvoker(PluginValue returned) : IPluginAutomationInvoker
    {
        public ValueTask<PluginDispatchInvocationOutcome> InvokeAutomationAsync(
            PluginAutomationEndpoint endpoint,
            PluginInvocationContext.Automation context,
            PluginValue input,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PluginDispatchInvocationOutcome>(
                new PluginDispatchInvocationOutcome.Returned(returned)
            );
    }
}
