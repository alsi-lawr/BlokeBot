using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

internal sealed partial class PluginAutomationCatalogRegistry
{
    private static void ValidateTemplateStructure(
        PluginAutomationTemplateDescriptor template,
        IReadOnlyDictionary<PluginAutomationDefinitionId, IPluginAutomationDefinition> definitions
    )
    {
        var nodes = new Dictionary<PluginTemplateNodeId, IPluginAutomationDefinition>();
        foreach (var node in template.Nodes)
        {
            if (
                !definitions.TryGetValue(node.DefinitionId, out var definition)
                || !nodes.TryAdd(node.Id, definition)
            )
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' references an invalid or cross-feature definition."
                );
            }
        }
        if (
            nodes.Values.Count(definition =>
                definition.Descriptor.Kind == AutomationNodeKind.Source
            ) != 1
        )
        {
            throw new PluginAutomationPlanException(
                $"Template '{template.Id.Value}' must declare exactly one Source node."
            );
        }

        var incoming =
            new HashSet<(PluginTemplateNodeId NodeId, PluginAutomationFieldId InputId)>();
        foreach (var edge in template.Edges)
        {
            if (
                !nodes.TryGetValue(edge.FromNode, out var source)
                || !nodes.TryGetValue(edge.ToNode, out var target)
            )
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' has a Data connection with a missing node."
                );
            }
            var output = source.Endpoint.Descriptor.Outputs.SingleOrDefault(field =>
                field.Id == edge.FromOutput
            );
            var input = target.Endpoint.Descriptor.Inputs.SingleOrDefault(field =>
                field.Id == edge.ToInput
            );
            if (
                output is null
                || input is null
                || output.ValueKind != input.ValueKind
                || source.Descriptor.Kind == AutomationNodeKind.Action
            )
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' has an incompatible Data connection."
                );
            }
            if (!output.Required && input.Required)
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' connects an optional output to a required input."
                );
            }
            if (!incoming.Add((edge.ToNode, edge.ToInput)))
            {
                throw new PluginAutomationPlanException(
                    $"Template '{template.Id.Value}' connects more than one Data output to the same input."
                );
            }
        }
    }

    private static void EnsureAcyclic(
        IEnumerable<Guid> nodes,
        IEnumerable<PluginAutomationStoreEdge> edges
    )
    {
        var adjacency = nodes.ToDictionary(static id => id, static _ => new List<Guid>());
        foreach (var edge in edges)
        {
            adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
        }
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        if (adjacency.Keys.Any(Visit))
        {
            throw new PluginAutomationPlanException(
                "Plugin automation template contains a dependency cycle."
            );
        }

        return;

        bool Visit(Guid node)
        {
            if (visited.Contains(node))
            {
                return false;
            }
            if (!visiting.Add(node))
            {
                return true;
            }
            if (adjacency[node].Any(Visit))
            {
                return true;
            }
            _ = visiting.Remove(node);
            _ = visited.Add(node);
            return false;
        }
    }
}
