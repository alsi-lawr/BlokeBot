using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationFlowCanvas
{
    private const int _gridSize = 24;
    private ElementReference _root;
    private IJSObjectReference? _module;
    private DotNetObjectReference<AutomationFlowCanvas>? _self;
    private AutomationNodeId? _focusNodeAfterRender;
    private string? _renderSignature;

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationEditorNode> Nodes { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationFlowDraftEdge> Edges { get; set; } = [];

    [Parameter]
    public IReadOnlySet<AutomationNodeId> SelectedNodeIds { get; set; } =
        new HashSet<AutomationNodeId>();

    [Parameter]
    public AutomationNodeId? DisclosedNodeId { get; set; }

    [Parameter]
    public long DisclosureGeneration { get; set; }

    [Parameter]
    public Guid? SelectedEdgeId { get; set; }

    [Parameter]
    public IReadOnlyList<AutomationGraphError> Errors { get; set; } = [];

    [Parameter]
    public IReadOnlyList<AutomationSampleNodeOutcome> SampleOutcomes { get; set; } = [];

    [Parameter]
    public AutomationFlowCanvasSettings Settings { get; set; }

    [Parameter, EditorRequired]
    public string ViewportKey { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<AutomationCanvasSelectionRequest> SelectionChanged { get; set; }

    [Parameter]
    public EventCallback<AutomationCanvasSelectionRequest> PointerSelectionChanged { get; set; }

    [Parameter]
    public EventCallback<AutomationCanvasDisclosureRequest> DisclosureChanged { get; set; }

    [Parameter]
    public EventCallback<IReadOnlyList<AutomationNodeMoveRequest>> MoveNodes { get; set; }

    [Parameter]
    public EventCallback<IReadOnlyList<AutomationNodeId>> DeleteNodes { get; set; }

    [Parameter]
    public EventCallback<Guid> DeleteEdge { get; set; }

    [Parameter]
    public EventCallback<AutomationConnectionRequest> Connect { get; set; }

    [Parameter]
    public EventCallback ConnectionRejected { get; set; }

    [Parameter]
    public EventCallback<AutomationFlowCanvasSettings> SettingsChanged { get; set; }

    private bool _noNodeSelection => SelectedNodeIds.Count == 0;

    private int _canvasWidth => Math.Max(960, DisplayMax(static position => position.X) + 320);

    private int _canvasHeight => Math.Max(620, DisplayMax(static position => position.Y) + 260);

    private int _mobileCanvasHeight => Math.Max(496, 72 + (Nodes.Count * 132));

    private string _canvasViewportStyle =>
        FormattableString.Invariant($"--automation-mobile-canvas-height:{_mobileCanvasHeight}px");

    private string _canvasStageStyle =>
        FormattableString.Invariant($"width:{_canvasWidth}px;height:{_canvasHeight}px");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await _js.InvokeAsync<IJSObjectReference>(
                "import",
                "./Features/Automations/Page/AutomationFlowCanvas.razor.js"
            );
            _self = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("initialize", _root, _self);
        }

        var signature = RenderSignature();
        if (_module is not null && (firstRender || signature != _renderSignature))
        {
            _renderSignature = signature;
            await _module.InvokeVoidAsync("refresh", _root);
        }
        if (_module is not null)
        {
            if (_focusNodeAfterRender is { } nodeId)
            {
                _focusNodeAfterRender = null;
                await _module.InvokeVoidAsync("focusNode", _root, nodeId.Value.ToString("D"));
            }
        }
    }

    public void RestoreFocusAfterRender(AutomationNodeId nodeId) => _focusNodeAfterRender = nodeId;

    [JSInvokable]
    public Task MoveNodesFromCanvasAsync(AutomationNodeMovePayload[] moves) =>
        MoveNodes.InvokeAsync(
            moves
                .Select(static move => new AutomationNodeMoveRequest(
                    new(move.NodeId),
                    Snapped(move.X),
                    Snapped(move.Y)
                ))
                .ToArray()
        );

    [JSInvokable]
    public Task SetSelectionFromCanvasAsync(Guid[] nodeIds, Guid? edgeId) =>
        SelectionChanged.InvokeAsync(
            new(nodeIds.Select(static id => new AutomationNodeId(id)).ToArray(), edgeId)
        );

    [JSInvokable]
    public Task SetPointerSelectionFromCanvasAsync(Guid[] nodeIds) =>
        PointerSelectionChanged.InvokeAsync(
            new(nodeIds.Select(static id => new AutomationNodeId(id)).ToArray(), null)
        );

    [JSInvokable]
    public Task SetNodeDisclosureFromCanvasAsync(Guid? nodeId, long generation) =>
        DisclosureChanged.InvokeAsync(
            new(
                nodeId is { } disclosedNodeId ? new AutomationNodeId(disclosedNodeId) : null,
                generation
            )
        );

    [JSInvokable]
    public Task ToggleNodeSelectionFromCanvasAsync(Guid nodeId)
    {
        var selected = SelectedNodeIds.ToHashSet();
        var typedNodeId = new AutomationNodeId(nodeId);
        if (!selected.Add(typedNodeId))
        {
            _ = selected.Remove(typedNodeId);
        }

        return SelectionChanged.InvokeAsync(new(selected.ToArray(), null));
    }

    [JSInvokable]
    public Task DeleteSelectionFromCanvasAsync(Guid[] nodeIds, Guid? edgeId) =>
        edgeId is { } selectedEdge
            ? DeleteEdge.InvokeAsync(selectedEdge)
            : DeleteNodes.InvokeAsync(
                nodeIds.Select(static id => new AutomationNodeId(id)).ToArray()
            );

    [JSInvokable]
    public Task ConnectFromCanvasAsync(
        Guid sourceNodeId,
        string sourcePortId,
        Guid targetNodeId,
        string targetPortId
    ) =>
        Connect.InvokeAsync(
            new(new(sourceNodeId), new(sourcePortId), new(targetNodeId), new(targetPortId))
        );

    [JSInvokable]
    public Task RejectConnectionFromCanvasAsync() => ConnectionRejected.InvokeAsync();

    private Task SelectEdgeAsync(Guid edgeId) => SelectionChanged.InvokeAsync(new([], edgeId));

    private async Task HandleNodeKeyAsync(AutomationEditorNode node, KeyboardEventArgs args)
    {
        if (args.Key is "Delete" or "Backspace")
        {
            await DeleteNodes.InvokeAsync(
                SelectedNodeIds.Contains(node.Id) ? SelectedNodeIds.ToArray() : [node.Id]
            );
            return;
        }

        var movement = args.Key switch
        {
            "ArrowLeft" => (-_gridSize, 0),
            "ArrowRight" => (_gridSize, 0),
            "ArrowUp" => (0, -_gridSize),
            "ArrowDown" => (0, _gridSize),
            _ => (0, 0),
        };
        if (movement == (0, 0))
        {
            return;
        }

        var ids = SelectedNodeIds.Contains(node.Id)
            ? SelectedNodeIds
            : new HashSet<AutomationNodeId> { node.Id };
        await MoveNodes.InvokeAsync(
            MoveRequestsInDisplayDirection(ids, movement.Item1, movement.Item2)
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose", _root);
            await _module.DisposeAsync();
        }

        _self?.Dispose();
    }
}

public sealed record AutomationNodeMoveRequest(AutomationNodeId NodeId, int X, int Y);

public sealed record AutomationNodeMovePayload(Guid NodeId, int X, int Y);

public sealed record AutomationCanvasSelectionRequest(
    IReadOnlyList<AutomationNodeId> NodeIds,
    Guid? EdgeId
);

public sealed record AutomationCanvasDisclosureRequest(AutomationNodeId? NodeId, long Generation);

internal sealed record AutomationRetainedPort(AutomationPortId PortId, int Index, int Count);
