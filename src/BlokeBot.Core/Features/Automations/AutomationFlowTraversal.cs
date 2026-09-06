using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationFlowTraversal
{
    internal static ImmutableArray<Guid> Schedule(
        IEnumerable<AutomationRuntimeSerialization.PersistedEdge> edges,
        Guid sourceNodeId,
        string? sourcePort,
        HashSet<Guid> scheduled
    ) => [.. Outgoing(edges, sourceNodeId, sourcePort).Where(scheduled.Add)];

    internal static ImmutableArray<Guid> Outgoing(
        IEnumerable<AutomationRuntimeSerialization.PersistedEdge> edges,
        Guid sourceNodeId,
        string? sourcePort
    ) =>
        edges
            .Where(edge =>
                edge.Kind == AutomationEdgeKind.Flow
                && edge.SourceNodeId == sourceNodeId
                && (sourcePort is null || edge.SourcePortId == sourcePort)
            )
            .OrderBy(static edge => edge.SourcePortId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNodeId)
            .Select(static edge => edge.TargetNodeId)
            .ToImmutableArray();
}
