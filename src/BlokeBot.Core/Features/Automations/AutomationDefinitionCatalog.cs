using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationCatalogRegistrationException(string message) : Exception(message);

internal sealed partial class AutomationDefinitionCatalog
{
    internal const int SupportedSchemaVersion = 1;

    private readonly ImmutableDictionary<
        AutomationDefinitionId,
        IAutomationDefinition
    > _definitions;
    private readonly ImmutableDictionary<AutomationDefinitionId, AutomationModuleId> _modules;
    private readonly PluginAutomationCatalogRegistry _plugins;

    public AutomationDefinitionCatalog(
        IEnumerable<IAutomationCatalogModule> modules,
        PluginAutomationCatalogRegistry? plugins = null
    )
    {
        _plugins = plugins ?? new();
        var definitions = ImmutableDictionary.CreateBuilder<
            AutomationDefinitionId,
            IAutomationDefinition
        >();
        var definitionModules = ImmutableDictionary.CreateBuilder<
            AutomationDefinitionId,
            AutomationModuleId
        >();
        var moduleIds = new HashSet<AutomationModuleId>();
        foreach (var module in modules)
        {
            ValidateStableId(module.Id.Value, "module");
            if (!moduleIds.Add(module.Id))
            {
                throw new AutomationCatalogRegistrationException(
                    $"Automation module identifier '{module.Id.Value}' is registered more than once."
                );
            }

            foreach (var definition in module.Definitions)
            {
                Validate(definition.Descriptor, module.Id);
                if (!definitions.TryAdd(definition.Descriptor.Id, definition))
                {
                    throw new AutomationCatalogRegistrationException(
                        $"Automation definition identifier '{definition.Descriptor.Id.Value}' is registered more than once."
                    );
                }
                definitionModules.Add(definition.Descriptor.Id, module.Id);
            }
        }

        _definitions = definitions.ToImmutable();
        _modules = definitionModules.ToImmutable();
        ValidateTriggerContextRequirements(_definitions);
        _coreDescriptors = _definitions
            .Values.Select(static definition => definition.Descriptor)
            .OrderBy(static definition => definition.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private ImmutableArray<AutomationDefinitionDescriptor> _coreDescriptors { get; }

    internal ImmutableArray<AutomationDefinitionDescriptor> DescriptorsForHost(
        AutomationHostId hostId
    ) =>
        [
            .. _coreDescriptors
                .Concat(_plugins.DescriptorsForHost(hostId.Value))
                .OrderBy(static descriptor => descriptor.Id.Value, StringComparer.Ordinal),
        ];

    internal ImmutableArray<AutomationDefinitionDescriptor> Descriptors =>
        [
            .. _coreDescriptors
                .Concat(_plugins.Current.Descriptors)
                .OrderBy(static descriptor => descriptor.Id.Value, StringComparer.Ordinal),
        ];

    internal long Revision => _plugins.Current.Version;

    internal ValueTask<long> WaitForPluginChangeAsync(
        long observed,
        CancellationToken cancellationToken
    ) => _plugins.WaitForChangeAsync(observed, cancellationToken);

    internal bool TryResolve(AutomationDefinitionId id, out IAutomationDefinition definition)
    {
        if (_definitions.TryGetValue(id, out definition!))
        {
            return true;
        }
        if (_plugins.Current.Definitions.TryGetValue(id, out var plugin))
        {
            definition = plugin;
            return true;
        }
        definition = null!;
        return false;
    }

    internal bool TryResolve(
        AutomationHostId hostId,
        AutomationDefinitionId id,
        out IAutomationDefinition definition
    )
    {
        if (_definitions.TryGetValue(id, out definition!))
        {
            return true;
        }
        if (_plugins.TryResolve(id, hostId.Value, out var plugin))
        {
            definition = plugin;
            return true;
        }
        definition = null!;
        return false;
    }

    internal bool IsFormat1Definition(AutomationDefinitionId id) =>
        _modules.TryGetValue(id, out var module)
        && module.Value
            is "blokebot.core"
                or "blokebot.native-operations"
                or "blokebot.twitch-events"
                or "blokebot.competitions";

    internal static bool IsValidEffectiveDescriptor(
        AutomationDefinitionDescriptor registered,
        AutomationDefinitionDescriptor effective
    )
    {
        if (
            effective.Id != registered.Id
            || effective.Kind != registered.Kind
            || effective.Scope != registered.Scope
            || effective.Schema != registered.Schema
            || effective.Capabilities != registered.Capabilities
            || effective.RetrySafety != registered.RetrySafety
            || effective.TriggerContextRequirement != registered.TriggerContextRequirement
            || effective.PluginProvenance != registered.PluginProvenance
        )
        {
            return false;
        }

        try
        {
            Validate(effective, new("effective"));
            return true;
        }
        catch (AutomationCatalogRegistrationException)
        {
            return false;
        }
    }

    private static void ValidateTriggerContextRequirements(
        IReadOnlyDictionary<AutomationDefinitionId, IAutomationDefinition> definitions
    )
    {
        foreach (var definition in definitions.Values)
        {
            var descriptor = definition.Descriptor;
            var requirement = descriptor.TriggerContextRequirement;
            if (requirement is null)
            {
                continue;
            }

            if (descriptor.Kind != AutomationNodeKind.Action)
            {
                throw new AutomationCatalogRegistrationException(
                    $"Automation definition '{descriptor.Id.Value}' is invalid. Only actions can declare a trigger requirement."
                );
            }

            if (
                requirement.CompatibleSources.IsEmpty
                || string.IsNullOrWhiteSpace(requirement.UnavailableReason)
                || string.IsNullOrWhiteSpace(requirement.ValidationMessage)
                || requirement.CompatibleSources.Any(sourceId =>
                    !definitions.TryGetValue(sourceId, out var source)
                    || source.Descriptor.Kind != AutomationNodeKind.Source
                )
            )
            {
                throw new AutomationCatalogRegistrationException(
                    $"Automation definition '{descriptor.Id.Value}' is invalid. Declare one or more registered trigger sources and complete user help."
                );
            }
        }
    }
}
