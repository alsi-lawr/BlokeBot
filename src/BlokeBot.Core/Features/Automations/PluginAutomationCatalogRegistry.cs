using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Automations;

internal sealed class PluginAutomationCatalogSnapshot
{
    internal PluginAutomationCatalogSnapshot(
        ImmutableDictionary<AutomationDefinitionId, IPluginAutomationDefinition> definitions,
        ImmutableDictionary<
            (AutomationDefinitionId DefinitionId, int HostId),
            IPluginAutomationDefinition
        > hostDefinitions,
        long version
    )
    {
        Definitions = definitions;
        HostDefinitions = hostDefinitions;
        Descriptors =
        [
            .. definitions
                .Values.Select(static definition => definition.Descriptor)
                .OrderBy(static descriptor => descriptor.Id.Value, StringComparer.Ordinal),
        ];
        Version = version;
    }

    internal ImmutableDictionary<
        AutomationDefinitionId,
        IPluginAutomationDefinition
    > Definitions { get; }

    internal ImmutableDictionary<
        (AutomationDefinitionId DefinitionId, int HostId),
        IPluginAutomationDefinition
    > HostDefinitions { get; }

    internal ImmutableArray<AutomationDefinitionDescriptor> Descriptors { get; }

    internal long Version { get; }

    internal static PluginAutomationCatalogSnapshot Empty { get; } = new([], [], 0);
}

internal sealed partial class PluginAutomationCatalogRegistry
    : IPluginAutomationCatalogSink,
        IPluginFeatureAutomationPlanner
{
    private static readonly JsonSerializerOptions _hashOptions = CreateHashOptions();
    private static readonly JsonSerializerOptions _provenanceOptions = new(
        JsonSerializerDefaults.Web
    );
    private readonly object _sync = new();
    private readonly Dictionary<PluginId, PluginFeatureDeclaration> _declarations = [];
    private PluginFeatureSnapshot _features = PluginFeatureSnapshot.Empty;
    private PluginAutomationCatalogSnapshot _current = PluginAutomationCatalogSnapshot.Empty;
    private TaskCompletionSource<long> _change = NewChangeCompletion();
    private long _version;

    internal PluginAutomationCatalogSnapshot Current => Volatile.Read(ref _current);

    internal ImmutableArray<AutomationDefinitionDescriptor> DescriptorsForHost(int hostId) =>
        [
            .. Current
                .HostDefinitions.Where(pair => pair.Key.HostId == hostId)
                .Select(static pair => pair.Value.Descriptor)
                .OrderBy(static descriptor => descriptor.Id.Value, StringComparer.Ordinal),
        ];

    internal bool TryResolve(
        AutomationDefinitionId definitionId,
        int hostId,
        out IPluginAutomationDefinition definition
    ) => Current.HostDefinitions.TryGetValue((definitionId, hostId), out definition!);

    internal ValueTask<long> WaitForChangeAsync(long observed, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return observed != _version
                ? ValueTask.FromResult(_version)
                : new(_change.Task.WaitAsync(cancellationToken));
        }
    }

    public void PublishDeclaration(PluginFeatureDeclaration declaration)
    {
        lock (_sync)
        {
            var hadPrevious = _declarations.TryGetValue(
                declaration.Installation.PluginId,
                out var previous
            );
            _declarations[declaration.Installation.PluginId] = declaration;
            try
            {
                RebuildLocked();
            }
            catch
            {
                if (hadPrevious)
                {
                    _declarations[declaration.Installation.PluginId] = previous!;
                }
                else
                {
                    _ = _declarations.Remove(declaration.Installation.PluginId);
                }
                throw;
            }
        }
    }

    public void RemoveDeclaration(PluginId pluginId, PluginLifecycleFence fence)
    {
        lock (_sync)
        {
            if (
                _declarations.TryGetValue(pluginId, out var declaration)
                && declaration.Fence == fence
            )
            {
                _ = _declarations.Remove(pluginId);
                RebuildLocked();
            }
        }
    }

    public void PublishFeatures(PluginFeatureSnapshot snapshot)
    {
        lock (_sync)
        {
            var previous = _features;
            _features = snapshot;
            try
            {
                RebuildLocked();
            }
            catch
            {
                _features = previous;
                throw;
            }
        }
    }

    internal static string SerializeProvenance(AutomationPluginProvenance provenance) =>
        JsonSerializer.Serialize(provenance, _provenanceOptions);

    internal static bool TryDeserializeProvenance(
        string? json,
        out AutomationPluginProvenance provenance
    )
    {
        provenance = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            provenance = JsonSerializer.Deserialize<AutomationPluginProvenance>(
                json,
                _provenanceOptions
            )!;
            return provenance is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void RebuildLocked()
    {
        var definitions = ImmutableDictionary.CreateBuilder<
            AutomationDefinitionId,
            IPluginAutomationDefinition
        >();
        var hostDefinitions = ImmutableDictionary.CreateBuilder<
            (AutomationDefinitionId DefinitionId, int HostId),
            IPluginAutomationDefinition
        >();
        foreach (var state in _features.States.Values.Where(static state => state.Enabled))
        {
            if (
                !_declarations.TryGetValue(state.Key.PluginId, out var declaration)
                || declaration.Fence != state.Fence
                || declaration.FindFeature(state.Key.FeatureId) is null
            )
            {
                continue;
            }

            foreach (var definition in BuildDefinitions(declaration, state).Values)
            {
                if (
                    !hostDefinitions.TryAdd(
                        (definition.Descriptor.Id, state.Key.HostId.Value),
                        definition
                    )
                )
                {
                    throw new AutomationCatalogRegistrationException(
                        $"Plugin automation definition '{definition.Descriptor.Id.Value}' collides for host {state.Key.HostId.Value}."
                    );
                }
                if (
                    definitions.TryGetValue(definition.Descriptor.Id, out var registered)
                    && !registered.Descriptor.PluginProvenance!.SameCode(
                        definition.Descriptor.PluginProvenance!
                    )
                )
                {
                    throw new AutomationCatalogRegistrationException(
                        $"Plugin automation definition '{definition.Descriptor.Id.Value}' collides with an active definition."
                    );
                }
                _ = definitions.TryAdd(definition.Descriptor.Id, definition);
            }
        }

        var change = _change;
        _change = NewChangeCompletion();
        Volatile.Write(
            ref _current,
            new(definitions.ToImmutable(), hostDefinitions.ToImmutable(), ++_version)
        );
        _ = change.TrySetResult(_version);
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, _hashOptions))
        );

    private static JsonSerializerOptions CreateHashOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        );
        return options;
    }

    private static TaskCompletionSource<long> NewChangeCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class PluginAutomationPlanException(string message) : Exception(message);
}
