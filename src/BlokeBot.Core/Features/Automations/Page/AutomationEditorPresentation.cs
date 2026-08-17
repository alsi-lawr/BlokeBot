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

internal enum AutomationToolboxCategory
{
    Triggers,
    Values,
    Transforms,
    Control,
    Actions,
}

internal sealed record AutomationToolboxItem(
    AutomationDefinitionDescriptor Definition,
    AutomationToolboxCategory Category,
    bool IsAvailable,
    string Availability,
    int Relevance
);

internal static class AutomationToolboxCatalog
{
    internal static ImmutableArray<AutomationToolboxCategory> Categories { get; } =
        Enum.GetValues<AutomationToolboxCategory>().ToImmutableArray();

    internal static ImmutableArray<AutomationToolboxItem> Query(
        IEnumerable<AutomationDefinitionDescriptor> definitions,
        AutomationToolboxCategory activeCategory,
        string search,
        Func<AutomationDefinitionDescriptor, (bool Available, string Reason)> availability,
        IEnumerable<AutomationDefinitionDescriptor>? contextualDefinitions = null
    )
    {
        var query = search.Trim();
        var searching = query.Length > 0;
        var contexts = (contextualDefinitions ?? [])
            .GroupBy(static definition => definition.Id)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        return definitions
            .Select(definition =>
            {
                var state = availability(definition);
                return new AutomationToolboxItem(
                    definition,
                    Category(definition.Kind),
                    state.Available,
                    state.Reason,
                    Relevance(definition, contexts.GetValueOrDefault(definition.Id) ?? [], query)
                );
            })
            .Where(item =>
                searching ? item.Relevance < int.MaxValue : item.Category == activeCategory
            )
            .OrderBy(static item => item.Relevance)
            .ThenBy(static item => item.Category)
            .ThenBy(static item => item.Definition.Display.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Definition.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static string CategoryLabel(AutomationToolboxCategory category) =>
        category switch
        {
            AutomationToolboxCategory.Triggers => "Triggers",
            AutomationToolboxCategory.Values => "Values",
            AutomationToolboxCategory.Transforms => "Transforms",
            AutomationToolboxCategory.Control => "Control",
            AutomationToolboxCategory.Actions => "Actions",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    internal static AutomationToolboxCategory Category(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => AutomationToolboxCategory.Triggers,
            AutomationNodeKind.Value => AutomationToolboxCategory.Values,
            AutomationNodeKind.Transform => AutomationToolboxCategory.Transforms,
            AutomationNodeKind.Control => AutomationToolboxCategory.Control,
            AutomationNodeKind.Action => AutomationToolboxCategory.Actions,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static int Relevance(
        AutomationDefinitionDescriptor definition,
        IReadOnlyList<AutomationDefinitionDescriptor> contexts,
        string search
    )
    {
        var relevance = DefinitionRelevance(definition, search);
        return contexts.Aggregate(
            relevance,
            (current, context) => Math.Min(current, DefinitionRelevance(context, search))
        );
    }

    private static int DefinitionRelevance(AutomationDefinitionDescriptor definition, string search)
    {
        var name = definition.Display.Name;
        return search.Length == 0 ? 0
            : name.Equals(search, StringComparison.OrdinalIgnoreCase) ? 0
            : name.StartsWith(search, StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains(search, StringComparison.OrdinalIgnoreCase) ? 2
            : MessagePurposeRelevance(definition.Id, search) is { } purposeRelevance
                ? purposeRelevance
            : definition.Display.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                ? 8
            : definition.Display.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ? 9
            : definition.Inputs.Any(port => PortMatches(port, search))
            || definition.Outputs.Any(port => PortMatches(port, search))
                ? 10
            : int.MaxValue;
    }

    private static int? MessagePurposeRelevance(AutomationDefinitionId id, string search) =>
        !search.Equals("message", StringComparison.OrdinalIgnoreCase) ? null
        : id == AutomationDefinitionIds.CelTransform ? 3
        : id == AutomationDefinitionIds.ChatNotificationSource ? 4
        : id == AutomationDefinitionIds.SendShoutoutAction ? 5
        : id == AutomationDefinitionIds.IncomingRaidSource ? 6
        : id == AutomationDefinitionIds.ConditionControl ? 7
        : null;

    private static bool PortMatches(AutomationPortMetadata port, string search) =>
        port.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || port.ValueType.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
}

internal sealed record AutomationConnectionCompatibility(bool IsCompatible, string Reason)
{
    internal static AutomationConnectionCompatibility Compatible { get; } =
        new(true, "Correct type");
}

internal static class AutomationConnections
{
    internal static AutomationEdgeKind Kind(AutomationPortMetadata output) =>
        output.ValueType == AutomationPortValueType.Flow
            ? AutomationEdgeKind.Flow
            : AutomationEdgeKind.Data;

    internal static AutomationConnectionCompatibility Compatibility(
        AutomationNodeKind sourceKind,
        AutomationPortMetadata output,
        AutomationPortMetadata input
    ) =>
        (output.ValueType, input.ValueType, sourceKind) switch
        {
            (AutomationPortValueType.Flow, AutomationPortValueType.Flow, _) =>
                AutomationConnectionCompatibility.Compatible,
            (AutomationPortValueType.Flow, _, _) => new(
                false,
                "Flow outputs connect only to Flow inputs."
            ),
            (_, _, _)
                when input.ValueType == AutomationPortValueType.Flow
                    || output.ValueType != input.ValueType => new(
                false,
                $"Expected {TypeLabel(input)}. The selected source port supplies {TypeLabel(output)}."
            ),
            (_, _, AutomationNodeKind.Action) => new(
                false,
                "Use Data from a trigger, Value, Transform, or Control node."
            ),
            (_, _, _)
                when output.Nullability == AutomationPortNullability.Nullable
                    && input.Nullability == AutomationPortNullability.NonNullable => new(
                false,
                $"Expected {TypeLabel(input)}. The selected source can be null."
            ),
            (_, _, _)
                when output.Sensitivity == AutomationDataSensitivity.Sensitive
                    && input.Sensitivity == AutomationDataSensitivity.Safe => new(
                false,
                "This input cannot accept Sensitive Data."
            ),
            _ => AutomationConnectionCompatibility.Compatible,
        };

    internal static AutomationConnectionCompatibility Compatibility(
        AutomationEditorNode source,
        AutomationPortMetadata output,
        AutomationEditorNode target,
        AutomationPortMetadata input
    ) =>
        source.Id == target.Id
            ? new(false, "Choose a different node.")
            : Compatibility(source.Definition.Kind, output, input);

    internal static string? Issue(
        AutomationFlowDraftEdge edge,
        IReadOnlyList<AutomationEditorNode> nodes
    )
    {
        var source = nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var target = nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        if (source is null || target is null)
        {
            return "A saved node is not available.";
        }

        var output = source.Definition.Outputs.FirstOrDefault(port => port.Id == edge.SourcePortId);
        var input = target.Definition.Inputs.FirstOrDefault(port => port.Id == edge.TargetPortId);
        if (output is null || input is null)
        {
            return "A saved port is not available.";
        }
        if (Kind(output) != edge.Kind)
        {
            return edge.Kind == AutomationEdgeKind.Flow
                ? "The selected ports do not carry Flow."
                : "The selected ports do not carry Data.";
        }

        var compatibility = Compatibility(source, output, target, input);
        return compatibility.IsCompatible ? null : compatibility.Reason;
    }

    internal static string TypeLabel(AutomationPortMetadata port) =>
        $"{port.ValueType}{(port.Nullability == AutomationPortNullability.Nullable ? "?" : string.Empty)}";
}

internal sealed record AutomationCelCompletion(string Name, AutomationPortValueType ValueType);

internal static class AutomationCelCompletions
{
    internal static ImmutableArray<AutomationCelCompletion> ForRestrictedInput(
        AutomationPortMetadata input
    ) =>
        input.ValueType == AutomationPortValueType.Arguments
            ? [new("arguments", AutomationPortValueType.Arguments)]
            : [];

    internal static ImmutableArray<AutomationCelCompletion> ForOutput(
        AutomationEditorNode transform
    ) =>
        transform
            .TransformInputs.SelectMany(static input => InputCompletions(input))
            .ToImmutableArray();

    private static IEnumerable<AutomationCelCompletion> InputCompletions(
        AutomationCelTransformInput input
    )
    {
        yield return new(input.Identifier.Value, input.ValueType);
        if (input.ValueType is AutomationPortValueType.Actor or AutomationPortValueType.Channel)
        {
            yield return new(
                $"{input.Identifier.Value}.display_name",
                AutomationPortValueType.Text
            );
            yield return new($"{input.Identifier.Value}.login", AutomationPortValueType.Text);
        }
        else if (input.ValueType == AutomationPortValueType.Stream)
        {
            yield return new($"{input.Identifier.Value}.title", AutomationPortValueType.Text);
            yield return new($"{input.Identifier.Value}.game_name", AutomationPortValueType.Text);
            yield return new(
                $"{input.Identifier.Value}.started_at",
                AutomationPortValueType.Timestamp
            );
        }
    }
}
