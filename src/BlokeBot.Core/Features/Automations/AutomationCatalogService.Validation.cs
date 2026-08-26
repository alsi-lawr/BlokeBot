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
        bool requireCurrentExecution
    ) =>
        !_catalog.TryResolve(hostId, definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolvedPersisted(
                definition,
                schemaVersion,
                configuration,
                persistedProvenance,
                requireCurrentExecution
            );

    private AutomationConfigurationCheck ValidateEnabledPersisted(
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration,
        AutomationPluginProvenance? persistedProvenance,
        bool requireCurrentExecution
    ) =>
        !_catalog.TryResolve(definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolvedPersisted(
                definition,
                schemaVersion,
                configuration,
                persistedProvenance,
                requireCurrentExecution
            );

    private AutomationConfigurationCheck ValidateResolvedPersisted(
        IAutomationDefinition definition,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration,
        AutomationPluginProvenance? persistedProvenance,
        bool requireCurrentExecution
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
            : definition.Parse(configuration) switch
            {
                AutomationConfigurationParseResult.Invalid invalid =>
                    new AutomationConfigurationCheck.Invalid(invalid.Errors),
                AutomationConfigurationParseResult.Parsed parsed => ValidateDefinition(
                    definition,
                    parsed.Configuration
                ),
                _ => throw new UnreachableException(),
            };
    }

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
        AutomationConfiguration configuration
    )
    {
        var validation = definition.Validate(configuration);
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
