using System.Text.Json;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationCommandSourceCompatibility
{
    internal static async Task<CustomCommandConfigurationSaveFailure.ArgumentDependency?> CheckAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyDictionary<int, CustomCommandArgumentSchema?> proposed,
        CancellationToken cancellationToken
    )
    {
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(flow => flow.Nodes)
            .Include(flow => flow.Edges)
            .Where(flow => flow.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var current = await AutomationCommandSource.LoadAsync(db, new(hostId), cancellationToken);
        foreach (var flow in flows)
        {
            foreach (
                var source in flow.Nodes.Where(node =>
                    node.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
                )
            )
            {
                using var json = JsonDocument.Parse(source.ConfigurationJson);
                if (
                    !json.RootElement.TryGetProperty("custom-command-id", out var property)
                    || !property.TryGetInt32(out var commandId)
                    || !current.TryGetValue(commandId, out var oldSchema)
                )
                {
                    continue;
                }
                var next = proposed.GetValueOrDefault(commandId);
                foreach (
                    var edge in flow.Edges.Where(edge =>
                        edge.SourceNodeId == source.Id
                        && edge.Kind == PersistedAutomationEdgeKind.Data
                    )
                )
                {
                    var old = oldSchema.Fields.FirstOrDefault(field =>
                        CustomCommandArguments.PortId(field.Name) == edge.SourcePortId
                    );
                    if (old is null)
                    {
                        continue;
                    }
                    var replacement = next?.Fields.FirstOrDefault(field => field.Name == old.Name);
                    if (
                        replacement is null
                        || old.Kind == CustomCommandArgumentKind.Number
                            != (replacement.Kind == CustomCommandArgumentKind.Number)
                    )
                    {
                        return new(commandId, flow.Id, source.Id, old.Name, flow.Name);
                    }
                }
            }
        }
        return null;
    }

    internal static async Task<CustomCommandConfigurationSaveFailure.ArgumentDependency?> CheckFinalAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyDictionary<int, CustomCommandArgumentSchema> previousSchemas,
        AutomationCatalogService? catalog,
        CancellationToken cancellationToken
    )
    {
        var schemas = await AutomationCommandSource.LoadAsync(db, new(hostId), cancellationToken);
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .Include(flow => flow.Nodes)
            .Include(flow => flow.Edges)
            .Where(flow => flow.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        foreach (var flow in flows)
        {
            foreach (
                var source in flow.Nodes.Where(node =>
                    node.DefinitionId == AutomationDefinitionIds.CustomCommandSource.Value
                )
            )
            {
                using var json = JsonDocument.Parse(source.ConfigurationJson);
                if (
                    !json.RootElement.TryGetProperty("custom-command-id", out var property)
                    || !property.TryGetInt32(out var commandId)
                    || !previousSchemas.TryGetValue(commandId, out var previous)
                )
                {
                    continue;
                }
                _ = schemas.TryGetValue(commandId, out var next);
                foreach (
                    var edge in flow.Edges.Where(edge =>
                        edge.SourceNodeId == source.Id
                        && edge.Kind == PersistedAutomationEdgeKind.Data
                    )
                )
                {
                    var oldField = previous.Fields.FirstOrDefault(field =>
                        CustomCommandArguments.PortId(field.Name) == edge.SourcePortId
                    );
                    if (oldField is null)
                    {
                        continue;
                    }
                    var field = next?.Fields.FirstOrDefault(value => value.Name == oldField.Name);
                    if (field is not null && ValueType(field) == ValueType(oldField))
                    {
                        continue;
                    }
                    if (
                        field is null
                        || !TargetAccepts(
                            catalog,
                            hostId,
                            flow.Nodes.Single(node => node.Id == edge.TargetNodeId),
                            edge.TargetPortId,
                            ValueType(field)
                        )
                    )
                    {
                        return new(commandId, flow.Id, source.Id, oldField.Name, flow.Name);
                    }
                }
            }
        }
        return null;
    }

    private static AutomationPortValueType ValueType(CustomCommandArgumentField field) =>
        field.Kind switch
        {
            CustomCommandArgumentKind.Number => AutomationPortValueType.Number,
            CustomCommandArgumentKind.Token or CustomCommandArgumentKind.RemainingText =>
                AutomationPortValueType.Text,
        };

    private static bool TargetAccepts(
        AutomationCatalogService? catalog,
        int hostId,
        AutomationFlowNode target,
        string portId,
        AutomationPortValueType valueType
    )
    {
        if (
            catalog is null
            || AutomationRuntimeSerialization.RestoreInputBindings(
                target.InputBindingsJson,
                new(target.DefinitionId)
            )
                is not AutomationInputBindingsRestoreOutcome.Available bindings
        )
        {
            return false;
        }
        AutomationPluginProvenance? provenance = null;
        if (
            target.PluginProvenanceJson is not null
            && !PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                target.PluginProvenanceJson,
                out provenance
            )
        )
        {
            return false;
        }
        using var configuration = JsonDocument.Parse(target.ConfigurationJson);
        return catalog.ValidatePreparedDefinition(
                new(hostId),
                new(
                    target.DefinitionId,
                    target.DefinitionSchemaVersion,
                    configuration.RootElement,
                    provenance
                ),
                bindings.Bindings
            )
                is AutomationConfigurationCheck.Valid valid
            && valid.Definition.Inputs.Any(port =>
                port.Id.Value == portId && port.ValueType == valueType
            );
    }
}
