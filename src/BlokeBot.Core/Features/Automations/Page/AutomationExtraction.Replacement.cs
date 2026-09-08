namespace BlokeBot.Core.Features.Automations.Page;

internal static partial class AutomationExtraction
{
    internal static AutomationEditorState Replace(
        AutomationEditorState editor,
        AutomationExtractionPreview preview,
        AutomationSubflowRevision published,
        AutomationCatalogService catalog
    )
    {
        var position = editor.Nodes.First(node => preview.Selection.Contains(node.Id)).Position;
        var caller = AutomationEditorNode.FromDefinition(
            AutomationSubflowDefinitions.Invocation(published),
            catalog,
            position,
            published.Graph.Name
        );
        foreach (var input in preview.Inputs)
        {
            caller.SetBindingMode(new(input.Port.Id.Value), AutomationInputBindingMode.Connected);
        }
        var nodes = editor
            .Nodes.Where(node => !preview.Selection.Contains(node.Id))
            .Append(caller)
            .ToArray();
        var edges = editor
            .Edges.Where(edge =>
                !preview.Selection.Contains(edge.SourceNodeId)
                && !preview.Selection.Contains(edge.TargetNodeId)
            )
            .ToList();
        var boundaryEdges = new HashSet<(AutomationEdgeKind, AutomationNodeId, AutomationPortId)>();
        foreach (
            var edge in preview.Incoming.Where(edge =>
                edge.Kind == AutomationEdgeKind.Flow || IsActiveDataEdge(editor, edge)
            )
        )
        {
            if (!boundaryEdges.Add((edge.Kind, edge.SourceNodeId, edge.SourcePortId)))
            {
                continue;
            }
            var port =
                edge.Kind == AutomationEdgeKind.Flow
                    ? new AutomationPortId("flow")
                    : preview
                        .Inputs.Single(value =>
                            value.NodeId == edge.SourceNodeId && value.PortId == edge.SourcePortId
                        )
                        .Port.Id;
            edges.Add(edge with { TargetNodeId = caller.Id, TargetPortId = port });
        }
        foreach (var edge in preview.Outgoing)
        {
            var port =
                edge.Kind == AutomationEdgeKind.Flow
                    ? new AutomationPortId("complete")
                    : preview
                        .Outputs.Single(value =>
                            value.NodeId == edge.SourceNodeId && value.PortId == edge.SourcePortId
                        )
                        .Port.Id;
            edges.Add(edge with { SourceNodeId = caller.Id, SourcePortId = port });
        }
        var draft = editor.Draft(default) with
        {
            Nodes = [.. nodes.Select(node => node.Draft())],
            Edges = [.. edges],
        };
        var replacement = AutomationEditorState.Restore(
            draft,
            nodes.ToDictionary(node => node.Id, node => node.Definition)
        );
        replacement.Subflow = editor.Subflow;
        return replacement;
    }
}
