using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationCatalogService
{
    private readonly AutomationDefinitionCatalog _catalog;
    private readonly HostFeatureService _features;

    internal AutomationCatalogService(
        AutomationDefinitionCatalog catalog,
        HostFeatureService features,
        AutomationExpressionService? expressions = null,
        IEnumerable<IAutomationPureNodeHandler>? handlers = null,
        IAutomationIntegerEntropy? integerEntropy = null,
        PluginAutomationExecutionService? pluginExecution = null
    )
    {
        _catalog = catalog;
        _features = features;
        Data = new(
            this,
            new(catalog, handlers ?? []),
            expressions ?? new(),
            integerEntropy ?? new AutomationProductionIntegerEntropy(),
            pluginExecution
        );
    }

    internal AutomationDataResolver Data { get; }

    internal long CurrentRevision => _catalog.Revision;

    internal ValueTask<long> WaitForChangeAsync(
        long observed,
        CancellationToken cancellationToken
    ) => _catalog.WaitForPluginChangeAsync(observed, cancellationToken);

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
                            .. _catalog
                                .DescriptorsForHost(hostId)
                                .Where(descriptor =>
                                    enabled.Contains(
                                        NativeOperationAutomations.BackingFeature(
                                            descriptor.Id.Value
                                        )
                                    )
                                ),
                        ],
                        _catalog.Revision
                    )
                    : new AutomationCatalogSnapshot(
                        AutomationCatalogAvailability.Disabled,
                        [],
                        _catalog.Revision
                    ),
            () =>
                new AutomationCatalogSnapshot(
                    AutomationCatalogAvailability.HostNotFound,
                    [],
                    _catalog.Revision
                )
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
            persisted.Configuration,
            persisted.PluginProvenance,
            requireCurrentExecution: false
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

    internal bool TryResolvePlugin(
        AutomationHostId hostId,
        AutomationDefinitionId definitionId,
        out IPluginAutomationDefinition definition
    )
    {
        if (
            _catalog.TryResolve(hostId, definitionId, out var resolved)
            && resolved is IPluginAutomationDefinition plugin
        )
        {
            definition = plugin;
            return true;
        }
        definition = null!;
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
            ? ValidateEnabledPersisted(
                hostId,
                definitionId,
                schemaVersion,
                persisted.Configuration,
                persisted.PluginProvenance,
                requireCurrentExecution: true
            )
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
                hostId,
                definitionId,
                schemaVersion,
                persisted.Configuration,
                persisted.PluginProvenance,
                requireCurrentExecution: false
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
                hostId,
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
            ? ValidateEnabled(hostId, definitionId, schemaVersion, configuration)
            : new AutomationConfigurationCheck.HostNotFound();

    private async Task<bool> HostExistsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var result = await _features.Load(hostId.Value).RunAsync(cancellationToken);
        return result.Match(static _ => true, static () => false);
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
