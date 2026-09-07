using System.Collections.Immutable;
using System.Text;
using BlokeBot.Persistence;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationScenarioService
{
    internal Task<AutomationGraphValidation> ValidatePortableAsync(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture,
        CancellationToken cancellationToken,
        bool transfer = false,
        AutomationSubflowClosure? resolved = null,
        BlokeBotDbContext? preparationDb = null
    ) => ValidateAsync(draft, fixture, cancellationToken, transfer, resolved, preparationDb);

    private async Task<AutomationGraphValidation> ValidateAsync(
        AutomationFlowDraft draft,
        AutomationScenarioFixture fixture,
        CancellationToken cancellationToken,
        bool transfer = false,
        AutomationSubflowClosure? resolved = null,
        BlokeBotDbContext? preparationDb = null
    )
    {
        if (
            draft.Nodes.Length > MaximumGraphNodes
            || draft.Edges.Length > MaximumGraphEdges
            || fixture.ConnectedInputs.IsDefault
            || fixture.Configurations.IsDefault
            || fixture.Effects.IsDefault
            || fixture.ConnectedInputs.Length > MaximumGraphEdges
            || fixture.Configurations.Length > MaximumGraphNodes
            || fixture.Effects.Length > MaximumGraphNodes
        )
        {
            return new(
                null,
                [new(null, "scenario-too-large", "Reduce the scenario graph or fixture inputs.")]
            );
        }
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        if (fixture.Context.HostId != draft.HostId)
        {
            errors.Add(new(null, "scenario-host-mismatch", "Use a fixture for this channel."));
        }
        var source = draft.Nodes.FirstOrDefault(node => node.Id == fixture.SourceNodeId);
        if (
            source is null
            || source.Definition.TypeId != fixture.SourceDefinitionId.Value
            || source.Definition.SchemaVersion != fixture.SourceSchemaVersion.Value
            || fixture.Context.Event.SourceDefinitionId != fixture.SourceDefinitionId
            || catalog.ValidatePersistedDefinition(source.Definition)
                is not AutomationConfigurationCheck.Valid
                {
                    Definition.Kind: AutomationNodeKind.Source
                }
        )
        {
            errors.Add(
                new(
                    fixture.SourceNodeId,
                    "scenario-source-stale",
                    "Select the current trigger and recreate its fixture fields."
                )
            );
        }
        var replacements = new HashSet<AutomationNodeId>();
        foreach (var replacement in fixture.Configurations)
        {
            var node = draft.Nodes.FirstOrDefault(node => node.Id == replacement.NodeId);
            if (
                !replacements.Add(replacement.NodeId)
                || node is null
                || node.Definition.TypeId != replacement.Definition.TypeId
                || node.Definition.SchemaVersion != replacement.Definition.SchemaVersion
            )
            {
                errors.Add(
                    new(
                        replacement.NodeId,
                        "scenario-configuration-stale",
                        "Select the current node and configuration schema."
                    )
                );
            }
        }
        if (errors.Count > 0)
        {
            return new(null, errors.ToImmutable());
        }
        var configured = ApplyConfigurations(draft, fixture);
        var validation =
            resolved is not null
                ? await flows.ValidatePreparedAsync(
                    configured,
                    resolved,
                    transfer
                        ? AutomationFlowService.AutomationGraphAdmission.ConfigurationTransfer
                        : AutomationFlowService.AutomationGraphAdmission.Scenario,
                    cancellationToken,
                    preparationDb!
                )
            : transfer
                ? await flows.ValidateConfigurationTransferAsync(configured, cancellationToken)
            : await flows.ValidateScenarioAsync(configured, cancellationToken);
        if (validation.Gate is not null)
        {
            return validation;
        }
        errors.AddRange(validation.Errors);
        foreach (var node in configured.Nodes)
        {
            if (
                catalog.ValidatePersistedDefinition(node.Definition)
                    is AutomationConfigurationCheck.Valid valid
                && !AutomationScenarioSimulation.Supports(valid, catalog.Data)
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "scenario-simulation-unsupported",
                        "This definition has no isolated simulation contract. Remove it from the test graph."
                    )
                );
            }
        }
        var connected = new HashSet<(AutomationNodeId, AutomationPortId)>();
        foreach (var input in fixture.ConnectedInputs)
        {
            var node = configured.Nodes.FirstOrDefault(node => node.Id == input.NodeId);
            if (
                !connected.Add((input.NodeId, input.PortId))
                || node is null
                || catalog.ValidatePersistedDefinition(node.Definition)
                    is not AutomationConfigurationCheck.Valid valid
                || valid.Definition.Inputs.FirstOrDefault(port => port.Id == input.PortId)
                    is not { BindingFieldId: { } field } port
                || node.InputBindings.GetValueOrDefault(field)?.Mode
                    != AutomationInputBindingMode.Connected
                || input.Value.Sensitivity != AutomationDataSensitivity.Safe
                || port.Sensitivity != AutomationDataSensitivity.Safe
                || !AutomationScenarioSerialization.ValidValue(input.Value.Value)
                || !AutomationDataResolver.Matches(port, input.Value.Value)
            )
            {
                errors.Add(
                    new(
                        input.NodeId,
                        "scenario-connected-input-invalid",
                        "Use a schema-valid safe value for a connected input.",
                        PortId: input.PortId
                    )
                );
            }
        }
        var effects = new HashSet<AutomationNodeId>();
        foreach (var effect in fixture.Effects)
        {
            var node = configured.Nodes.FirstOrDefault(node => node.Id == effect.NodeId);
            if (
                !effects.Add(effect.NodeId)
                || !Enum.IsDefined(effect.Result)
                || node is null
                || catalog.ValidatePersistedDefinition(node.Definition)
                    is not AutomationConfigurationCheck.Valid
                    {
                        Definition.Kind: AutomationNodeKind.Action
                    }
            )
            {
                errors.Add(
                    new(
                        effect.NodeId,
                        "scenario-effect-invalid",
                        "Declare one success or failure result for an action node."
                    )
                );
            }
        }
        ValidateSourceContext(configured, fixture, errors);
        if (
            fixture.Context.Arguments.IsDefault
            || fixture.Context.Arguments.Select(arg => arg.Position).Distinct().Count()
                != fixture.Context.Arguments.Length
            || fixture.Context.Arguments.Any(arg => arg.Position < 0)
            || fixture
                .Context.Variables.ForExecution()
                .Any(pair =>
                    !Enum.IsDefined(pair.Value.Sensitivity)
                    || !AutomationScenarioSerialization.ValidValue(pair.Value.Value)
                )
        )
        {
            errors.Add(
                new(
                    fixture.SourceNodeId,
                    "scenario-context-invalid",
                    "Use unique non-negative argument positions and declared value sensitivity."
                )
            );
        }
        if (
            errors.Count == 0
            && Encoding.UTF8.GetByteCount(AutomationScenarioSerialization.Serialize(fixture))
                > MaximumFixtureBytes
        )
        {
            errors.Add(
                new(
                    null,
                    "scenario-too-large",
                    $"Use at most {MaximumFixtureBytes} bytes of fixture data."
                )
            );
        }
        return new(null, errors.ToImmutable());
    }
}
