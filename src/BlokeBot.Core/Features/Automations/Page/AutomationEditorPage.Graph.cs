using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationEditorPage
{
    private void AddNode(AutomationDefinitionDescriptor definition)
    {
        if (
            _editor is null
            || !AutomationNodeAvailability.Evaluate(definition, _editor.Nodes).IsAvailable
        )
        {
            return;
        }

        var node = _editor.AddNode(definition);
        ApplyFirstReferenceDefaults(node);
        _selectedNodeId = node.Id;
        SetSingleNodeSelection(node.Id);
        _nodeLibraryOpen = false;
        _mobileInspectorOpen = true;
        _focusInspectorAfterRender = true;
        EditorChanged();
    }

    private void ApplyFirstReferenceDefaults(AutomationEditorNode node)
    {
        foreach (
            var field in node.Definition.Configuration.Where(static field =>
                field.FieldType is AutomationConfigurationFieldType.Reference
            )
        )
        {
            var reference = (AutomationConfigurationFieldType.Reference)field.FieldType;
            if (
                _referenceChoices.TryGetValue(reference.ReferenceKind, out var choices)
                && choices.FirstOrDefault() is { } first
            )
            {
                node.SetValue(field.Id, first.Value);
            }
        }
    }

    private void RenameFlow(ChangeEventArgs args)
    {
        if (_editor is not null)
        {
            _editor.Name = args.Value?.ToString() ?? string.Empty;
            EditorChanged();
        }
    }

    private void ConnectNodes(AutomationConnectionRequest request)
    {
        if (
            _editor is null
            || ConnectionDetails(_editor, request) is not { } connection
            || _editor.Edges.Any(edge =>
                edge.SourceNodeId == request.SourceNodeId
                && edge.SourcePortId == request.SourcePortId
                && edge.TargetNodeId == request.TargetNodeId
                && edge.TargetPortId == request.TargetPortId
            )
            || (
                connection.Kind == AutomationEdgeKind.Data
                && _editor.Edges.Any(edge =>
                    edge.Kind == AutomationEdgeKind.Data
                    && edge.TargetNodeId == request.TargetNodeId
                    && edge.TargetPortId == request.TargetPortId
                )
            )
        )
        {
            return;
        }

        var edge = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            connection.Kind,
            request.SourceNodeId,
            request.SourcePortId,
            request.TargetNodeId,
            request.TargetPortId
        );
        _editor.Edges.Add(edge);
        _selectedEdgeId = edge.Id;
        _disclosedNodeId = null;
        _selectedNodeIds.Clear();
        _selectedNodeId = null;
        EditorChanged();
    }

    private void RepairConnection(AutomationRepairConnectionRequest request)
    {
        if (_editor is null || ConnectionDetails(_editor, request.Connection) is not { } connection)
        {
            return;
        }

        var index = _editor.Edges.FindIndex(edge => edge.Id == request.EdgeId);
        if (index < 0)
        {
            return;
        }

        _editor.Edges[index] = _editor.Edges[index] with
        {
            Kind = connection.Kind,
            SourceNodeId = request.Connection.SourceNodeId,
            SourcePortId = request.Connection.SourcePortId,
            TargetNodeId = request.Connection.TargetNodeId,
            TargetPortId = request.Connection.TargetPortId,
        };
        _selectedEdgeId = request.EdgeId;
        _disclosedNodeId = null;
        EditorChanged();
    }

    private void RejectConnection() =>
        ShowTimedValidationFeedback(
            "Release the connection on one compatible input or node.",
            failed: true
        );

    private static AutomationConnectionDetails? ConnectionDetails(
        AutomationEditorState editor,
        AutomationConnectionRequest request
    )
    {
        var source = editor.Nodes.FirstOrDefault(node => node.Id == request.SourceNodeId);
        var target = editor.Nodes.FirstOrDefault(node => node.Id == request.TargetNodeId);
        var output = source?.Definition.Outputs.FirstOrDefault(port =>
            port.Id == request.SourcePortId
        );
        var input = target?.Definition.Inputs.FirstOrDefault(port =>
            port.Id == request.TargetPortId
        );
        if (source is null || target is null || output is null || input is null)
        {
            return null;
        }

        var compatibility = AutomationConnections.Compatibility(source, output, target, input);
        return compatibility.IsCompatible ? new(AutomationConnections.Kind(output)) : null;
    }

    private void DeleteEdge(Guid edgeId)
    {
        _ = _editor?.Edges.RemoveAll(edge => edge.Id == edgeId);
        _selectedEdgeId = null;
        EditorChanged();
    }

    private void DeleteNode(AutomationNodeId nodeId) => DeleteNodes([nodeId]);

    private void DeleteNodes(IReadOnlyList<AutomationNodeId> nodeIds)
    {
        if (_editor is null || nodeIds.Count == 0)
        {
            return;
        }

        foreach (var nodeId in nodeIds)
        {
            _editor.RemoveNode(nodeId);
            _ = _selectedNodeIds.Remove(nodeId);
            if (_disclosedNodeId == nodeId)
            {
                _disclosedNodeId = null;
            }
        }

        _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.Single() : null;
        _selectedEdgeId = null;
        EditorChanged();
    }

    private void MoveNode(AutomationNodeMoveRequest request) => MoveNodes([request]);

    private void MoveNodes(IReadOnlyList<AutomationNodeMoveRequest> requests)
    {
        if (_editor is null)
        {
            return;
        }

        foreach (var request in requests)
        {
            var node = _editor.Nodes.FirstOrDefault(candidate => candidate.Id == request.NodeId);
            if (node is null)
            {
                continue;
            }

            node.Position = new(new(request.X), new(request.Y));
        }

        EditorChanged();
    }

    private void ChangeCanvasSettings(AutomationFlowCanvasSettings settings)
    {
        if (_editor is null)
        {
            return;
        }

        _editor.Canvas = settings;
        EditorChanged();
    }

    private void ChangeCanvasSelection(AutomationCanvasSelectionRequest selection)
    {
        ApplyCanvasSelection(selection);
        _disclosedNodeId = null;
    }

    private void ChangeCanvasPointerSelection(AutomationCanvasSelectionRequest selection)
    {
        ApplyCanvasSelection(selection);
        if (
            _disclosedNodeId is { } disclosed
            && (_selectedNodeIds.Count != 1 || !_selectedNodeIds.Contains(disclosed))
        )
        {
            _disclosedNodeId = null;
        }
    }

    private void ApplyCanvasSelection(AutomationCanvasSelectionRequest selection)
    {
        _selectedNodeIds.Clear();
        foreach (var nodeId in selection.NodeIds)
        {
            _ = _selectedNodeIds.Add(nodeId);
        }

        _selectedNodeId = _selectedNodeIds.Count == 1 ? _selectedNodeIds.Single() : null;
        _selectedEdgeId = selection.EdgeId;
        _mobileInspectorOpen = false;
        _inspectorFocusMode = _selectedNodeId is null ? null : AutomationEditorMode.Grid;
    }
}
