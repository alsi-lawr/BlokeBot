using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationCatalogService
{
    private readonly AutomationDefinitionCatalog _catalog;
    private readonly HostFeatureService _features;

    internal AutomationCatalogService(
        AutomationDefinitionCatalog catalog,
        HostFeatureService features,
        AutomationExpressionService? expressions = null,
        IEnumerable<IAutomationPureNodeHandler>? handlers = null,
        IAutomationIntegerEntropy? integerEntropy = null
    )
    {
        _catalog = catalog;
        _features = features;
        Data = new(
            this,
            new(catalog, handlers ?? []),
            expressions ?? new(),
            integerEntropy ?? new AutomationProductionIntegerEntropy()
        );
    }

    internal AutomationDataResolver Data { get; }

    public async Task<AutomationCatalogSnapshot> DiscoverAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var result = await _features.Load(hostId.Value).RunAsync(cancellationToken);
        return result.Match(
            enabled =>
                enabled.Contains(HostFeatureFlags.Automations)
                    ? new AutomationCatalogSnapshot(
                        AutomationCatalogAvailability.Enabled,
                        [
                            .. _catalog.Descriptors.Where(descriptor =>
                                enabled.Contains(
                                    NativeOperationAutomations.BackingFeature(descriptor.Id.Value)
                                )
                            ),
                        ]
                    )
                    : new AutomationCatalogSnapshot(AutomationCatalogAvailability.Disabled, []),
            static () =>
                new AutomationCatalogSnapshot(AutomationCatalogAvailability.HostNotFound, [])
        );
    }

    public Task<AutomationConfigurationCheck> ValidateForSaveAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration,
        CancellationToken cancellationToken
    ) => ValidateAsync(hostId, definitionId, schemaVersion, configuration, cancellationToken);

    public Task<AutomationConfigurationCheck> ValidatePersistedForSaveAsync(
        AutomationHostId hostId,
        PersistedAutomationNodeDefinition persisted,
        CancellationToken cancellationToken
    ) => ValidatePersistedAsync(hostId, persisted, cancellationToken);

    internal AutomationConfigurationCheck ValidatePersistedDefinition(
        PersistedAutomationNodeDefinition persisted
    ) =>
        ValidateEnabledPersisted(
            new(persisted.TypeId),
            new(persisted.SchemaVersion),
            persisted.Configuration
        );

    public bool TryDescribe(
        AutomationDefinitionId definitionId,
        out AutomationDefinitionDescriptor descriptor
    )
    {
        if (_catalog.TryResolve(definitionId, out var definition))
        {
            descriptor = definition.Descriptor;
            return true;
        }

        descriptor = null!;
        return false;
    }

    internal bool IsFormat1Definition(string definitionId) =>
        _catalog.IsFormat1Definition(new(definitionId));

    public async Task<AutomationConfigurationCheck> ValidateBeforeExecutionAsync(
        AutomationHostId requestedHostId,
        AutomationContext context,
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration,
        CancellationToken cancellationToken
    ) =>
        requestedHostId != context.HostId
            ? new AutomationConfigurationCheck.HostMismatch(requestedHostId, context.HostId)
            : await ValidateAdmittedAsync(
                requestedHostId,
                definitionId,
                schemaVersion,
                configuration,
                cancellationToken
            );

    public async Task<AutomationConfigurationCheck> ValidatePersistedBeforeExecutionAsync(
        AutomationHostId requestedHostId,
        AutomationContext context,
        PersistedAutomationNodeDefinition persisted,
        CancellationToken cancellationToken
    ) =>
        requestedHostId != context.HostId
            ? new AutomationConfigurationCheck.HostMismatch(requestedHostId, context.HostId)
            : await ValidateAdmittedPersistedAsync(requestedHostId, persisted, cancellationToken);

    private async Task<AutomationConfigurationCheck> ValidateAdmittedPersistedAsync(
        AutomationHostId hostId,
        PersistedAutomationNodeDefinition persisted,
        CancellationToken cancellationToken
    )
    {
        var definitionId = new AutomationDefinitionId(persisted.TypeId);
        var schemaVersion = new AutomationSchemaVersion(persisted.SchemaVersion);
        return await HostExistsAsync(hostId, cancellationToken)
            ? ValidateEnabledPersisted(definitionId, schemaVersion, persisted.Configuration)
            : new AutomationConfigurationCheck.HostNotFound();
    }

    private async Task<AutomationConfigurationCheck> ValidatePersistedAsync(
        AutomationHostId hostId,
        PersistedAutomationNodeDefinition persisted,
        CancellationToken cancellationToken
    )
    {
        var definitionId = new AutomationDefinitionId(persisted.TypeId);
        var schemaVersion = new AutomationSchemaVersion(persisted.SchemaVersion);
        var availability = await AvailabilityAsync(hostId, cancellationToken);
        return availability switch
        {
            AutomationCatalogAvailability.HostNotFound =>
                new AutomationConfigurationCheck.HostNotFound(),
            AutomationCatalogAvailability.Disabled =>
                new AutomationConfigurationCheck.FeatureDisabled(),
            AutomationCatalogAvailability.Enabled => ValidateEnabledPersisted(
                definitionId,
                schemaVersion,
                persisted.Configuration
            ),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<AutomationConfigurationCheck> ValidateAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        var availability = await AvailabilityAsync(hostId, cancellationToken);
        return availability switch
        {
            AutomationCatalogAvailability.HostNotFound =>
                new AutomationConfigurationCheck.HostNotFound(),
            AutomationCatalogAvailability.Disabled =>
                new AutomationConfigurationCheck.FeatureDisabled(),
            AutomationCatalogAvailability.Enabled => ValidateEnabled(
                definitionId,
                schemaVersion,
                configuration
            ),
            _ => throw new UnreachableException(),
        };
    }

    private async Task<AutomationConfigurationCheck> ValidateAdmittedAsync(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        AutomationConfiguration configuration,
        CancellationToken cancellationToken
    ) =>
        await HostExistsAsync(hostId, cancellationToken)
            ? ValidateEnabled(definitionId, schemaVersion, configuration)
            : new AutomationConfigurationCheck.HostNotFound();

    private async Task<bool> HostExistsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var result = await _features.Load(hostId.Value).RunAsync(cancellationToken);
        return result.Match(static _ => true, static () => false);
    }

    private AutomationConfigurationCheck ValidateEnabledPersisted(
        AutomationDefinitionId definitionId,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration
    ) =>
        !_catalog.TryResolve(definitionId, out var definition)
            ? new AutomationConfigurationCheck.DefinitionMissing(definitionId)
            : ValidateResolvedPersisted(definition, schemaVersion, configuration);

    private AutomationConfigurationCheck ValidateResolvedPersisted(
        IAutomationDefinition definition,
        AutomationSchemaVersion schemaVersion,
        JsonElement configuration
    )
    {
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

    private async Task<AutomationCatalogAvailability> AvailabilityAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var result = await _features.Load(hostId.Value).RunAsync(cancellationToken);
        return result.Match(
            static enabled =>
                enabled.Contains(HostFeatureFlags.Automations)
                    ? AutomationCatalogAvailability.Enabled
                    : AutomationCatalogAvailability.Disabled,
            static () => AutomationCatalogAvailability.HostNotFound
        );
    }
}
