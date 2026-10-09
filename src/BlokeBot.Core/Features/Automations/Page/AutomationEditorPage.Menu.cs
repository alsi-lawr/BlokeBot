using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

[JsonConverter(typeof(JsonStringEnumConverter<AutomationEditorMenuAction>))]
public enum AutomationEditorMenuAction
{
    Undo,
    Redo,
    Delete,
}

public sealed record AutomationEditorMenuInvocation(
    string EditorKey,
    long Revision,
    Guid[] NodeIds,
    Guid? EdgeId
);

public sealed record AutomationEditorMenuView(
    AutomationEditorMenuInvocation Invocation,
    string Context,
    string Target,
    string History,
    bool Undo,
    bool Redo,
    bool Delete,
    string Note
);

public partial class AutomationEditorPage
{
    private string _menuContext =>
        $"{_canvasViewportKey}|{_draftRevision}|{_busy}|{_selectedEdgeId}|{string.Join(',', _selectedNodeIds.OrderBy(id => id.Value))}";

    [JSInvokable]
    public async Task<AutomationEditorMenuView?> PrepareEditorMenuAsync(
        Guid[]? nodeIds,
        Guid? edgeId
    )
    {
        AutomationEditorMenuView? result = null;
        await InvokeAsync(() =>
        {
            if (_editor is null || _busy)
            {
                return;
            }

            var nodes = (nodeIds ?? _selectedNodeIds.Select(id => id.Value).ToArray())
                .Select(id => new AutomationNodeId(id))
                .Where(id => _editor.Nodes.Any(node => node.Id == id))
                .ToArray();
            var edge =
                edgeId is { } id && _editor.Edges.Any(candidate => candidate.Id == id)
                    ? edgeId
                    : null;
            if (
                nodeIds is not null
                && (
                    _selectedEdgeId != edge
                    || !_selectedNodeIds.SetEquals(edge is null ? nodes : [])
                )
            )
            {
                ChangeCanvasPointerSelection(new(edge is null ? nodes : [], edge));
                StateHasChanged();
            }
            var target =
                edge is not null ? "Connection"
                : nodes.Length == 1 ? _editor.Nodes.First(node => node.Id == nodes[0]).EffectiveName
                : nodes.Length > 1 ? $"{nodes.Length} nodes selected"
                : "No selection";
            result = new AutomationEditorMenuView(
                new(
                    _canvasViewportKey,
                    _draftRevision,
                    nodes.Select(node => node.Value).ToArray(),
                    edge
                ),
                _menuContext,
                target,
                "Flow history",
                _history.UndoCount > 0,
                _history.RedoCount > 0,
                edge is not null || nodes.Length > 0,
                _history.UndoCount > 0 || _history.RedoCount > 0
                    ? "Selection Delete is undoable."
                    : "No edits in this history."
            );
        });
        return result;
    }

    [JSInvokable]
    public Task ApplyEditorMenuAsync(
        AutomationEditorMenuAction action,
        AutomationEditorMenuInvocation invocation
    ) =>
        InvokeAsync(() =>
        {
            if (
                _editor is null
                || _busy
                || invocation.EditorKey != _canvasViewportKey
                || invocation.Revision != _draftRevision
                || invocation.EdgeId != _selectedEdgeId
                || !_selectedNodeIds.SetEquals(
                    invocation.NodeIds.Select(id => new AutomationNodeId(id))
                )
            )
            {
                return;
            }

            switch (action)
            {
                case AutomationEditorMenuAction.Undo:
                    Undo();
                    break;
                case AutomationEditorMenuAction.Redo:
                    Redo();
                    break;
                case AutomationEditorMenuAction.Delete:
                    if (invocation.EdgeId is { } edgeId)
                    {
                        DeleteEdge(edgeId);
                    }
                    else
                    {
                        DeleteNodes(
                            invocation.NodeIds.Select(id => new AutomationNodeId(id)).ToArray()
                        );
                    }
                    break;
            }

            StateHasChanged();
        });
}
