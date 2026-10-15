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
                )
                {
                    continue;
                }
                foreach (
                    var edge in flow.Edges.Where(edge =>
                        edge.SourceNodeId == source.Id
                        && edge.Kind == PersistedAutomationEdgeKind.Data
                        && edge.SourcePortId.StartsWith("argument.", StringComparison.Ordinal)
                    )
                )
                {
                    if (
                        !schemas.TryGetValue(commandId, out var schema)
                        || !schema.Fields.Any(field =>
                            CustomCommandArguments.PortId(field.Name) == edge.SourcePortId
                        )
                    )
                    {
                        return new(commandId, flow.Id, source.Id, edge.SourcePortId, flow.Name);
                    }
                    var field = schema.Fields.Single(value =>
                        CustomCommandArguments.PortId(value.Name) == edge.SourcePortId
                    );
                    var target = flow.Nodes.Single(node => node.Id == edge.TargetNodeId);
                    var bindings = AutomationRuntimeSerialization.RestoreInputBindings(
                        target.InputBindingsJson,
                        new(target.DefinitionId)
                    );
                    if (catalog is null)
                    {
                        return new(commandId, flow.Id, source.Id, field.Name, flow.Name);
                    }
                    using var targetJson = JsonDocument.Parse(target.ConfigurationJson);
                    if (
                        bindings is AutomationInputBindingsRestoreOutcome.Available restored
                        && catalog.ValidatePersistedDefinition(
                            new(
                                target.DefinitionId,
                                target.DefinitionSchemaVersion,
                                targetJson.RootElement
                            ),
                            restored.Bindings
                        )
                            is AutomationConfigurationCheck.Valid valid
                        && valid.Definition.Inputs.SingleOrDefault(port =>
                            port.Id.Value == edge.TargetPortId
                        )
                            is { } input
                        && input.ValueType
                            != (
                                field.Kind == CustomCommandArgumentKind.Number
                                    ? AutomationPortValueType.Number
                                    : AutomationPortValueType.Text
                            )
                    )
                    {
                        return new(commandId, flow.Id, source.Id, field.Name, flow.Name);
                    }
                }
            }
        }
        return null;
    }
}
