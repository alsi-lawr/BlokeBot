using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationFlowTraversal
{
    internal static ImmutableArray<Guid> Schedule(
        IEnumerable<AutomationRuntimeSerialization.PersistedEdge> edges,
        Guid sourceNodeId,
        AutomationOutputSelection selection,
        HashSet<Guid> scheduled
    ) => [.. Outgoing(edges, sourceNodeId, selection).Where(scheduled.Add)];

    internal static ImmutableArray<Guid> Outgoing(
        IEnumerable<AutomationRuntimeSerialization.PersistedEdge> edges,
        Guid sourceNodeId,
        AutomationOutputSelection selection
    )
    {
        var ports = selection.Match<HashSet<string>?>(
            static _ => null,
            static selected =>
                selected.Ports.Select(static port => port.Value).ToHashSet(StringComparer.Ordinal)
        );
        return edges
            .Where(edge =>
                edge.Kind == AutomationEdgeKind.Flow
                && edge.SourceNodeId == sourceNodeId
                && (ports is null || ports.Contains(edge.SourcePortId))
            )
            .OrderBy(static edge => edge.SourcePortId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetNodeId)
            .Select(static edge => edge.TargetNodeId)
            .ToImmutableArray();
    }
}

internal abstract record AutomationOutputSelection
{
    private AutomationOutputSelection() { }

    internal ImmutableArray<AutomationPortId?> BranchPorts =>
        Match<ImmutableArray<AutomationPortId?>>(
            static _ => [null],
            static selected => [.. selected.Ports.Select(static port => (AutomationPortId?)port)]
        );

    internal abstract TResult Match<TResult>(
        Func<All, TResult> all,
        Func<Selected, TResult> selected
    );

    internal sealed record All : AutomationOutputSelection
    {
        internal override TResult Match<TResult>(
            Func<All, TResult> all,
            Func<Selected, TResult> selected
        ) => all(this);
    }

    internal sealed record Selected(ImmutableArray<AutomationPortId> Ports)
        : AutomationOutputSelection
    {
        internal override TResult Match<TResult>(
            Func<All, TResult> all,
            Func<Selected, TResult> selected
        ) => selected(this);
    }
}
