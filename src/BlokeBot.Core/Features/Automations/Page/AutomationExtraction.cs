using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationExtractionBoundary(
    AutomationPortMetadata Port,
    AutomationNodeId NodeId,
    AutomationPortId PortId
);

internal sealed record AutomationExtractionPreview(
    AutomationSubflowDraft? Subflow,
    ImmutableArray<AutomationGraphError> Errors,
    ImmutableArray<AutomationExtractionBoundary> Inputs,
    ImmutableArray<AutomationExtractionBoundary> Outputs,
    ImmutableArray<AutomationFlowDraftEdge> Incoming,
    ImmutableArray<AutomationFlowDraftEdge> Outgoing,
    ImmutableHashSet<AutomationNodeId> Selection
)
{
    internal bool IsValid => Subflow is not null && Errors.IsEmpty;
}

internal static partial class AutomationExtraction
{
    internal static AutomationExtractionPreview Preview(
        AutomationEditorState editor,
        AutomationHostId host,
        IEnumerable<AutomationNodeId> selection,
        string name,
        AutomationSubflowId subflowId,
        AutomationCatalogService catalog
    )
    {
        var ids = selection.ToImmutableHashSet();
        var nodes = editor.Nodes.Where(node => ids.Contains(node.Id)).ToArray();
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        var incoming = editor
            .Edges.Where(edge =>
                !ids.Contains(edge.SourceNodeId) && ids.Contains(edge.TargetNodeId)
            )
            .ToImmutableArray();
        var outgoing = editor
            .Edges.Where(edge =>
                ids.Contains(edge.SourceNodeId) && !ids.Contains(edge.TargetNodeId)
            )
            .ToImmutableArray();
        var internalEdges = editor
            .Edges.Where(edge => ids.Contains(edge.SourceNodeId) && ids.Contains(edge.TargetNodeId))
            .ToImmutableArray();
        var controls = nodes
            .Where(node =>
                node.Definition.Kind is AutomationNodeKind.Action or AutomationNodeKind.Control
            )
            .ToArray();
        var controlIds = controls.Select(node => node.Id).ToHashSet();
        var links = internalEdges.Where(edge => edge.Kind == AutomationEdgeKind.Flow).ToArray();
        var heads = controls
            .Where(node => links.All(edge => edge.TargetNodeId != node.Id))
            .ToArray();
        var tails = controls
            .Where(node => links.All(edge => edge.SourceNodeId != node.Id))
            .ToArray();
        var flowIn = incoming.Where(edge => edge.Kind == AutomationEdgeKind.Flow).ToArray();
        var flowOut = outgoing.Where(edge => edge.Kind == AutomationEdgeKind.Flow).ToArray();
        if (
            nodes.Length == 0
            || nodes.Length != ids.Count
            || nodes.Any(node =>
                node.Definition.Kind == AutomationNodeKind.Source
                || node.Definition.Id.Value
                    is AutomationSubflowDefinitions.Entry
                        or AutomationSubflowDefinitions.Exit
            )
        )
        {
            errors.Add(
                Error(
                    "extraction-selection",
                    "Select actions, controls or their data nodes, without triggers or subflow boundaries."
                )
            );
        }
        if (
            controls.Length == 0
            || heads.Length != 1
            || tails.Length != 1
            || links.Length != controls.Length - 1
            || controls.Any(node =>
                node.Definition.Outputs.Count(port =>
                    port.ValueType == AutomationPortValueType.Flow
                ) != 1
            )
            || links.Any(edge =>
                !controlIds.Contains(edge.SourceNodeId) || !controlIds.Contains(edge.TargetNodeId)
            )
            || controls.Any(node =>
                links.Count(edge => edge.SourceNodeId == node.Id) > 1
                || links.Count(edge => edge.TargetNodeId == node.Id) > 1
            )
        )
        {
            errors.Add(
                Error(
                    "extraction-control-chain",
                    "Select one connected control chain with a single completion path."
                )
            );
        }
        if (
            heads.Length == 1
            && (flowIn.Length == 0 || flowIn.Any(edge => edge.TargetNodeId != heads[0].Id))
        )
        {
            errors.Add(
                Error(
                    "extraction-entry",
                    "Incoming flow edges must target the first selected control node."
                )
            );
        }
        if (tails.Length == 1 && flowOut.Any(edge => edge.SourceNodeId != tails[0].Id))
        {
            errors.Add(
                Error(
                    "extraction-exit",
                    "Outgoing flow edges must leave the last selected control node."
                )
            );
        }
        var inputs = Boundaries(
            incoming.Where(edge => IsActiveDataEdge(editor, edge)),
            editor,
            errors
        );
        var outputs = Boundaries(
            outgoing.Where(edge => edge.Kind == AutomationEdgeKind.Data),
            editor,
            errors
        );
        if (heads.Length == 1 && inputs.Length > 0)
        {
            var resolved = ResolvedInputs(editor, heads[0].Id);
            if (
                heads[0].FailurePolicy != AutomationNodeFailurePolicy.Stop
                || inputs.Any(input =>
                    !incoming.Any(edge =>
                        edge.SourceNodeId == input.NodeId
                        && edge.SourcePortId == input.PortId
                        && resolved.Contains(edge.Id)
                    )
                )
            )
            {
                errors.Add(
                    Error(
                        "extraction-input-order",
                        "Select the first input-consuming action with Stop; extraction would otherwise change input failure ordering."
                    )
                );
            }
        }
        if (errors.Count > 0)
        {
            return new(null, errors.ToImmutable(), inputs, outputs, incoming, outgoing, ids);
        }
        var contract = new AutomationSubflowInterface(
            [.. inputs.Select(value => value.Port)],
            [.. outputs.Select(value => value.Port)]
        );
        if (!AutomationSubflowDefinitions.ValidInterface(contract))
        {
            return new(
                null,
                [
                    Error(
                        "extraction-interface",
                        "The generated interface contains unsupported or conflicting port identifiers."
                    ),
                ],
                inputs,
                outputs,
                incoming,
                outgoing,
                ids
            );
        }
        var entry = AutomationEditorNode.FromDefinition(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, contract),
            catalog,
            new(new(20), new(70)),
            "Entry"
        );
        var exit = AutomationEditorNode.FromDefinition(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Exit, contract),
            catalog,
            new(new(750), new(70)),
            "Exit"
        );
        foreach (var output in outputs)
        {
            exit.SetBindingMode(new(output.Port.Id.Value), AutomationInputBindingMode.Connected);
        }
        var edges = internalEdges.ToBuilder();
        edges.Add(
            Edge(AutomationEdgeKind.Flow, entry.Id, new("complete"), heads[0].Id, new("flow"))
        );
        edges.Add(
            Edge(
                AutomationEdgeKind.Flow,
                tails[0].Id,
                controls
                    .Single(node => node.Id == tails[0].Id)
                    .Definition.Outputs.Single(port =>
                        port.ValueType == AutomationPortValueType.Flow
                    )
                    .Id,
                exit.Id,
                new("flow")
            )
        );
        foreach (var edge in incoming.Where(edge => IsActiveDataEdge(editor, edge)))
        {
            var input = inputs.Single(value =>
                value.NodeId == edge.SourceNodeId && value.PortId == edge.SourcePortId
            );
            edges.Add(edge with { SourceNodeId = entry.Id, SourcePortId = input.Port.Id });
        }
        foreach (var output in outputs)
        {
            edges.Add(
                Edge(AutomationEdgeKind.Data, output.NodeId, output.PortId, exit.Id, output.Port.Id)
            );
        }
        var graph = new AutomationFlowDraft(
            null,
            host,
            name,
            AutomationFlowSchema.CurrentVersion,
            false,
            [entry.Draft(), .. nodes.Select(node => node.Draft()), exit.Draft()],
            edges.ToImmutable(),
            editor.Canvas
        );
        return new(
            new(subflowId, name, contract, graph),
            [],
            inputs,
            outputs,
            incoming,
            outgoing,
            ids
        );
    }

    private static bool IsActiveDataEdge(AutomationEditorState editor, AutomationFlowDraftEdge edge)
    {
        var node = editor.Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        var field = node
            ?.Definition.Inputs.FirstOrDefault(port => port.Id == edge.TargetPortId)
            ?.BindingFieldId;
        return edge.Kind == AutomationEdgeKind.Data
            && node is not null
            && field is { } id
            && node.Binding(id).Mode == AutomationInputBindingMode.Connected;
    }

    private static HashSet<Guid> ResolvedInputs(
        AutomationEditorState editor,
        AutomationNodeId first
    )
    {
        var resolved = new HashSet<Guid>();
        var visited = new HashSet<AutomationNodeId>();
        var pending = new Stack<AutomationNodeId>();
        pending.Push(first);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node))
            {
                continue;
            }
            foreach (
                var edge in editor.Edges.Where(edge =>
                    edge.TargetNodeId == node && IsActiveDataEdge(editor, edge)
                )
            )
            {
                _ = resolved.Add(edge.Id);
                if (
                    editor
                        .Nodes.FirstOrDefault(source => source.Id == edge.SourceNodeId)
                        ?.Definition.Kind
                    is AutomationNodeKind.Value
                        or AutomationNodeKind.Transform
                )
                {
                    pending.Push(edge.SourceNodeId);
                }
            }
        }
        return resolved;
    }

    private static ImmutableArray<AutomationExtractionBoundary> Boundaries(
        IEnumerable<AutomationFlowDraftEdge> edges,
        AutomationEditorState editor,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var boundaries = ImmutableArray.CreateBuilder<AutomationExtractionBoundary>();
        foreach (var group in edges.GroupBy(edge => (edge.SourceNodeId, edge.SourcePortId)))
        {
            var port = editor
                .Nodes.FirstOrDefault(node => node.Id == group.Key.SourceNodeId)
                ?.Definition.Outputs.FirstOrDefault(port => port.Id == group.Key.SourcePortId);
            if (port is null)
            {
                errors.Add(
                    Error(
                        "extraction-port-missing",
                        "A boundary connection has no matching output port."
                    )
                );
                continue;
            }
            boundaries.Add(
                new(
                    port with
                    {
                        BindingFieldId = null,
                    },
                    group.Key.SourceNodeId,
                    group.Key.SourcePortId
                )
            );
        }
        foreach (
            var collision in boundaries
                .GroupBy(value => value.Port.Id)
                .Where(group => group.Count() > 1)
        )
        {
            errors.Add(
                Error(
                    "extraction-port-name",
                    $"The generated boundary name '{collision.Key.Value}' is used by different outputs. Rename a source port before extraction."
                )
            );
        }
        return boundaries.ToImmutable();
    }

    private static AutomationFlowDraftEdge Edge(
        AutomationEdgeKind kind,
        AutomationNodeId source,
        AutomationPortId sourcePort,
        AutomationNodeId target,
        AutomationPortId targetPort
    ) => new(Guid.NewGuid(), kind, source, sourcePort, target, targetPort);

    private static AutomationGraphError Error(string code, string message) =>
        new(null, code, message);
}
