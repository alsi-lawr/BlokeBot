using System.Diagnostics;
using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationCatalogService
{
    private AutomationConfigurationCheck ValidateEnabledPersisted(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration,
        AutomationPluginProvenance? persistedProvenance,
        bool requireCurrentExecution,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding>? bindings = null
    ) =>
        !_catalog.TryResolve(hostId, definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolvedPersisted(
                definition,
                schemaVersion,
                configuration,
                persistedProvenance,
                requireCurrentExecution,
                bindings
            );

    private AutomationConfigurationCheck ValidateEnabledPersisted(
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration,
        AutomationPluginProvenance? persistedProvenance,
        bool requireCurrentExecution,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding>? bindings = null
    ) =>
        !_catalog.TryResolve(definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolvedPersisted(
                definition,
                schemaVersion,
                configuration,
                persistedProvenance,
                requireCurrentExecution,
                bindings
            );

    private AutomationConfigurationCheck ValidateResolvedPersisted(
        IAutomationDefinition definition,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration,
        AutomationPluginProvenance? persistedProvenance,
        bool requireCurrentExecution,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding>? bindings =
            null,
        bool authoringDescriptor = false
    )
    {
        var currentProvenance = definition.Descriptor.PluginProvenance;
        if (
            (currentProvenance is null) != (persistedProvenance is null)
            || (
                currentProvenance is not null
                && persistedProvenance is not null
                && (
                    requireCurrentExecution
                        ? !currentProvenance.SameExecution(persistedProvenance)
                        : !currentProvenance.SameCode(persistedProvenance)
                )
            )
        )
        {
            return new AutomationConfigurationCheck.PluginProvenanceMismatch(
                definition.Descriptor.Id
            );
        }
        var compatibility = definition.Descriptor.Schema.Classify(schemaVersion);
        return compatibility != AutomationSchemaCompatibilityStatus.Current
            ? new AutomationConfigurationCheck.SchemaUnsupported(
                definition.Descriptor.Id,
                schemaVersion,
                compatibility
            )
            : (
                bindings is not null && definition is IAutomationInputBindingDefinition bound
                    ? bound.ParseForInputBindings(configuration)
                    : definition.Parse(configuration)
            ) switch
            {
                AutomationConfigurationParseResult.Invalid invalid =>
                    new AutomationConfigurationCheck.Invalid(invalid.Errors),
                AutomationConfigurationParseResult.Parsed parsed => ValidateDefinition(
                    definition,
                    parsed.Configuration,
                    bindings,
                    authoringDescriptor
                ),
                _ => throw new UnreachableException(),
            };
    }

    internal AutomationConfigurationCheck DescribeForAuthoring(
        AutomationHostId hostId,
        AutomationFlowDraftNode node
    ) =>
        !_catalog.TryResolve(hostId, new(node.Definition.TypeId), out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(new(node.Definition.TypeId))
            : ValidateResolvedPersisted(
                definition,
                new(node.Definition.SchemaVersion),
                node.Definition.Configuration,
                node.Definition.PluginProvenance,
                requireCurrentExecution: false,
                bindings: node.InputBindings,
                authoringDescriptor: AutomationRuntimeSerialization.RetainsInactiveExpression(
                    new(node.Definition.TypeId)
                )
            );

    private AutomationConfigurationCheck ValidateEnabled(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration
    ) =>
        !_catalog.TryResolve(hostId, definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolved(definition, schemaVersion, configuration);

    private AutomationConfigurationCheck ValidateEnabled(
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration
    ) =>
        !_catalog.TryResolve(definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolved(definition, schemaVersion, configuration);

    private AutomationConfigurationCheck ValidateResolved(
        IAutomationDefinition definition,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration
    )
    {
        var compatibility = definition.Descriptor.Schema.Classify(schemaVersion);
        return compatibility != AutomationSchemaCompatibilityStatus.Current
            ? new AutomationConfigurationCheck.SchemaUnsupported(
                definition.Descriptor.Id,
                schemaVersion,
                compatibility
            )
            : ValidateDefinition(definition, configuration);
    }

    private static AutomationConfigurationCheck ValidateDefinition(
        IAutomationDefinition definition,
        AutomationConfiguration configuration,
        IReadOnlyDictionary<AutomationConfigurationFieldId, AutomationInputBinding>? bindings =
            null,
        bool authoringDescriptor = false
    )
    {
        var validation =
            authoringDescriptor && configuration is AutomationCelTransformConfiguration transform
                ? AutomationCelTransform.ValidateDeclarations(transform)
            : authoringDescriptor
            && definition.Descriptor.Id == AutomationDefinitionIds.SendChatAction
                ? AutomationValidationResult.Valid
            : bindings is not null && definition is IAutomationInputBindingDefinition bound
                ? bound.ValidateForInputBindings(configuration, bindings)
            : definition.Validate(configuration);
        if (!validation.IsValid)
        {
            return new AutomationConfigurationCheck.Invalid(validation.Errors);
        }

        var effective = definition is IAutomationEffectiveDefinition effectiveDefinition
            ? effectiveDefinition.EffectiveDescriptor(configuration)
            : definition.Descriptor;
        return !AutomationDefinitionCatalog.IsValidEffectiveDescriptor(
            definition.Descriptor,
            effective
        )
            ? new AutomationConfigurationCheck.Invalid([
                new(
                    new AutomationValidationTarget.Definition(),
                    "The persisted automation schema is invalid."
                ),
            ])
            : new AutomationConfigurationCheck.Valid(effective, configuration);
    }
}
