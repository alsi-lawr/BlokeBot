using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    private static void ValidateSourceAvailability(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyCollection<AutomationFlowDraftNode> sources,
        IEnumerable<AutomationFlowDraftEdge> edges,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> flowAdjacency,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var dataInputs = edges
            .Where(static edge => edge.Kind == AutomationEdgeKind.Data)
            .GroupBy(static edge => edge.TargetNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.SourceNodeId).ToArray()
            );
        var sourceIds = sources.Select(static source => source.Id).ToHashSet();
        var cachedBackings = new Dictionary<AutomationNodeId, HashSet<AutomationNodeId>>();
        foreach (var edge in edges.Where(static edge => edge.Kind == AutomationEdgeKind.Data))
        {
            if (
                !nodes.TryGetValue(edge.TargetNodeId, out var target)
                || !definitions.TryGetValue(target.Id, out var targetDefinition)
                || targetDefinition.Kind
                    is not (AutomationNodeKind.Action or AutomationNodeKind.Control)
            )
            {
                continue;
            }

            var backings = SourceBackings(
                edge.SourceNodeId,
                sourceIds,
                dataInputs,
                cachedBackings,
                []
            );
            if (backings.Count == 0)
            {
                continue;
            }

            var reachingSources = sources
                .Where(source => Reachable([source.Id], flowAdjacency).Contains(edge.TargetNodeId))
                .Select(static source => source.Id)
                .ToHashSet();
            if (backings.Count != 1 || !reachingSources.SetEquals(backings))
            {
                errors.Add(
                    new(
                        edge.TargetNodeId,
                        "data-source-unavailable",
                        "This source Data is not available on every Flow path to the input."
                    )
                );
            }
        }
    }

    private static HashSet<AutomationNodeId> SourceBackings(
        AutomationNodeId nodeId,
        IReadOnlySet<AutomationNodeId> sources,
        IReadOnlyDictionary<AutomationNodeId, AutomationNodeId[]> dataInputs,
        IDictionary<AutomationNodeId, HashSet<AutomationNodeId>> cached,
        HashSet<AutomationNodeId> visiting
    )
    {
        if (cached.TryGetValue(nodeId, out var known))
        {
            return known;
        }

        if (!visiting.Add(nodeId))
        {
            return [];
        }

        var backings = sources.Contains(nodeId) ? new HashSet<AutomationNodeId> { nodeId } : [];
        if (dataInputs.TryGetValue(nodeId, out var producers))
        {
            foreach (var producer in producers)
            {
                backings.UnionWith(SourceBackings(producer, sources, dataInputs, cached, visiting));
            }
        }

        _ = visiting.Remove(nodeId);
        cached[nodeId] = backings;
        return backings;
    }

    private static HashSet<AutomationNodeId> Reachable(
        IEnumerable<AutomationNodeId> sources,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency
    )
    {
        var reached = new HashSet<AutomationNodeId>();
        var pending = new Stack<AutomationNodeId>();
        foreach (var source in sources)
        {
            pending.Push(source);
        }
        while (pending.TryPop(out var nodeId))
        {
            if (!reached.Add(nodeId))
            {
                continue;
            }

            foreach (var target in adjacency[nodeId])
            {
                pending.Push(target);
            }
        }

        return reached;
    }

    private static bool HasCycle(
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> adjacency
    )
    {
        var remainingIncoming = adjacency.Keys.ToDictionary(static id => id, static _ => 0);
        foreach (var targets in adjacency.Values)
        {
            foreach (var target in targets)
            {
                remainingIncoming[target]++;
            }
        }

        var ready = new Queue<AutomationNodeId>(
            remainingIncoming.Where(static pair => pair.Value == 0).Select(static pair => pair.Key)
        );
        var visited = 0;
        while (ready.TryDequeue(out var nodeId))
        {
            visited++;
            foreach (var target in adjacency[nodeId])
            {
                remainingIncoming[target]--;
                if (remainingIncoming[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        return visited != adjacency.Count;
    }
}
