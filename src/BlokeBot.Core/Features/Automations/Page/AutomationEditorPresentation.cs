using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationValidationPresentation(
    ImmutableArray<AutomationGraphError> VisibleErrors,
    int IssueCount
)
{
    internal static AutomationValidationPresentation Create(
        IEnumerable<AutomationGraphError> errors,
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges
    )
    {
        var retainedIssues = edges
            .Where(edge => AutomationConnections.Issue(edge, nodes) is not null)
            .ToArray();
        var reachable = FlowReachableNodes(nodes, edges);
        var visible =
            retainedIssues.Length == 0
                ? errors.ToImmutableArray()
                : errors
                    .Where(error =>
                        !IsFalseDisconnectedCascade(error, reachable)
                        && !IsFalseDataSourceCascade(error, nodes, edges)
                    )
                    .ToImmutableArray();
        var representedValidationErrors = visible.Count(error =>
            retainedIssues.Any(edge => Represents(error, edge))
        );
        return new(visible, retainedIssues.Length + visible.Length - representedValidationErrors);
    }

    internal static IReadOnlyList<string> NodeIssues(
        AutomationEditorNode node,
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges,
        IReadOnlyList<AutomationGraphError> errors
    ) =>
        errors
            .Where(error => error.NodeId == node.Id)
            .Select(ErrorLabel)
            .Concat(
                node.Definition.Inputs.Select(input =>
                    InputIssue(node, input, nodes, edges, errors)
                )
            )
            .Concat(
                edges
                    .Where(edge => edge.SourceNodeId == node.Id || edge.TargetNodeId == node.Id)
                    .Select(edge => AutomationConnections.Issue(edge, nodes))
            )
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static string? InputIssue(
        AutomationEditorNode node,
        AutomationPortMetadata input,
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges,
        IReadOnlyList<AutomationGraphError> errors
    )
    {
        var error = errors.FirstOrDefault(error =>
            error.NodeId == node.Id
            && (
                error.PortId == input.Id
                || (input.BindingFieldId is not null && error.FieldId == input.BindingFieldId)
            )
        );
        if (error is not null)
        {
            return ErrorLabel(error);
        }
        var edge = edges.FirstOrDefault(edge =>
            edge.Kind == AutomationEdgeKind.Data
            && edge.TargetNodeId == node.Id
            && edge.TargetPortId == input.Id
        );
        return edge is not null ? AutomationConnections.Issue(edge, nodes)
            : input.BindingFieldId is { } fieldId
            && node.Binding(fieldId).Mode == AutomationInputBindingMode.Connected
                ? "Choose a source."
            : null;
    }

    private static string ErrorLabel(AutomationGraphError error) =>
        error.Diagnostic is { } diagnostic
            ? $"{diagnostic.Summary}. {diagnostic.Correction}"
            : error.Message;

    private static bool IsFalseDisconnectedCascade(
        AutomationGraphError error,
        IReadOnlySet<AutomationNodeId> reachable
    ) =>
        error.Code == "node-disconnected"
        && error.NodeId is { } nodeId
        && reachable.Contains(nodeId);

    private static bool Represents(AutomationGraphError error, AutomationFlowDraftEdge edge) =>
        error.NodeId == edge.TargetNodeId
        && (error.PortId is null || error.PortId == edge.TargetPortId)
        && error.Code
            is "edge-kind-invalid"
                or "port-missing"
                or "flow-port-incompatible"
                or "data-type-incompatible"
                or "data-source-incompatible"
                or "data-nullability-incompatible"
                or "data-sensitivity-incompatible";

    private static bool IsFalseDataSourceCascade(
        AutomationGraphError error,
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges
    )
    {
        if (error.Code != "data-source-unavailable" || error.NodeId is not { } targetId)
        {
            return false;
        }

        var sourceIds = nodes
            .Where(static node => node.Definition.Kind == AutomationNodeKind.Source)
            .Select(static node => node.Id)
            .ToHashSet();
        var reachingSources = sourceIds
            .Where(sourceId => FlowReachableNodesFrom(sourceId, nodes, edges).Contains(targetId))
            .ToHashSet();
        var relevantBackings = edges
            .Where(edge => edge.Kind == AutomationEdgeKind.Data && edge.TargetNodeId == targetId)
            .Select(edge => SourceBackings(edge.SourceNodeId, sourceIds, edges, []))
            .Where(static backings => backings.Count > 0)
            .ToArray();
        return relevantBackings.Length > 0
            && relevantBackings.All(backings => backings.SetEquals(reachingSources));
    }

    private static HashSet<AutomationNodeId> FlowReachableNodes(
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges
    )
    {
        var adjacency = nodes.ToDictionary(
            static node => node.Id,
            static _ => new List<AutomationNodeId>()
        );
        foreach (var edge in edges.Where(static edge => edge.Kind == AutomationEdgeKind.Flow))
        {
            if (adjacency.TryGetValue(edge.SourceNodeId, out var targets))
            {
                targets.Add(edge.TargetNodeId);
            }
        }

        var reachable = new HashSet<AutomationNodeId>();
        var pending = new Stack<AutomationNodeId>(
            nodes
                .Where(static node => node.Definition.Kind == AutomationNodeKind.Source)
                .Select(static node => node.Id)
        );
        while (pending.TryPop(out var nodeId))
        {
            if (!reachable.Add(nodeId) || !adjacency.TryGetValue(nodeId, out var targets))
            {
                continue;
            }
            foreach (var target in targets)
            {
                pending.Push(target);
            }
        }
        return reachable;
    }

    private static HashSet<AutomationNodeId> FlowReachableNodesFrom(
        AutomationNodeId sourceId,
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges
    )
    {
        var nodeIds = nodes.Select(static node => node.Id).ToHashSet();
        var adjacency = edges
            .Where(edge =>
                edge.Kind == AutomationEdgeKind.Flow && nodeIds.Contains(edge.SourceNodeId)
            )
            .GroupBy(static edge => edge.SourceNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static edge => edge.TargetNodeId).ToArray()
            );
        var reachable = new HashSet<AutomationNodeId>();
        var pending = new Stack<AutomationNodeId>();
        pending.Push(sourceId);
        while (pending.TryPop(out var nodeId))
        {
            if (!reachable.Add(nodeId) || !adjacency.TryGetValue(nodeId, out var targets))
            {
                continue;
            }
            foreach (var target in targets)
            {
                pending.Push(target);
            }
        }
        return reachable;
    }

    private static HashSet<AutomationNodeId> SourceBackings(
        AutomationNodeId nodeId,
        IReadOnlySet<AutomationNodeId> sourceIds,
        IReadOnlyList<AutomationFlowDraftEdge> edges,
        HashSet<AutomationNodeId> visiting
    )
    {
        if (!visiting.Add(nodeId))
        {
            return [];
        }

        var backings = sourceIds.Contains(nodeId) ? new HashSet<AutomationNodeId> { nodeId } : [];
        foreach (
            var producerId in edges
                .Where(edge => edge.Kind == AutomationEdgeKind.Data && edge.TargetNodeId == nodeId)
                .Select(static edge => edge.SourceNodeId)
        )
        {
            backings.UnionWith(SourceBackings(producerId, sourceIds, edges, visiting));
        }
        _ = visiting.Remove(nodeId);
        return backings;
    }
}

internal static class AutomationNodePresentation
{
    internal static string Status(
        bool needsRepair,
        bool validated,
        AutomationScenarioNodeOutcome? outcome
    )
    {
        var configuration =
            needsRepair ? "Needs repair"
            : validated ? "Ready"
            : "Draft";
        return outcome is null ? configuration : $"{configuration} · {OutcomeLabel(outcome)}";
    }

    internal static string OutcomeLabel(AutomationScenarioNodeOutcome outcome) =>
        outcome.OutcomeCode switch
        {
            "source-received" => "Received",
            "condition-true" => "Yes branch",
            "condition-false" => "No branch",
            "delayed" => "Virtual delay",
            "action-simulated" => "Hypothetical",
            _ => outcome.State switch
            {
                AutomationNodeRunState.Failed => "Failed",
                AutomationNodeRunState.ContinuedAfterFailure => "Continued after failure",
                AutomationNodeRunState.Invalidated => "Invalidated",
                AutomationNodeRunState.Cancelled => "Cancelled",
                _ => outcome.OutcomeCode,
            },
        };
}
