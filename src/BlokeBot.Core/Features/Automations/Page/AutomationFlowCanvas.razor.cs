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
            await _module.InvokeVoidAsync("refresh", _root);
            _renderSignature = signature;
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

    private Task SelectNodeAsync(AutomationNodeId nodeId, bool toggle)
    {
        var selected = SelectedNodeIds.ToHashSet();
        if (toggle)
        {
            if (!selected.Add(nodeId))
            {
                _ = selected.Remove(nodeId);
            }
        }
        else
        {
            selected = [nodeId];
        }

        return SelectionChanged.InvokeAsync(new(selected.ToArray(), null));
    }

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

    private Task NudgeSelectedAsync(int x, int y) =>
        MoveNodes.InvokeAsync(MoveRequestsInDisplayDirection(SelectedNodeIds, x, y));

    private IReadOnlyList<AutomationNodeMoveRequest> MoveRequestsInDisplayDirection(
        IEnumerable<AutomationNodeId> ids,
        int x,
        int y
    ) =>
        Nodes
            .Where(node => ids.Contains(node.Id))
            .Select(node =>
            {
                var display = DisplayPosition(node.Position, Settings.Orientation);
                var moved = new AutomationCanvasPosition(
                    new(Math.Max(0, display.X.Value + x)),
                    new(Math.Max(0, display.Y.Value + y))
                );
                var position = DisplayPosition(moved, Settings.Orientation);
                return new AutomationNodeMoveRequest(node.Id, position.X.Value, position.Y.Value);
            })
            .ToArray();

    private Task ChangeOrientationAsync(ChangeEventArgs args) =>
        Enum.TryParse<AutomationFlowOrientation>(args.Value?.ToString(), out var orientation)
            ? SettingsChanged.InvokeAsync(Settings with { Orientation = orientation })
            : Task.CompletedTask;

    private Task ChangeEdgeStyleAsync(ChangeEventArgs args) =>
        Enum.TryParse<AutomationEdgeStyle>(args.Value?.ToString(), out var edgeStyle)
            ? SettingsChanged.InvokeAsync(Settings with { EdgeStyle = edgeStyle })
            : Task.CompletedTask;

    private int DisplayMax(Func<AutomationCanvasPosition, AutomationCanvasCoordinate> selector) =>
        Nodes.Count == 0
            ? 0
            : Nodes.Max(node =>
                selector(DisplayPosition(node.Position, Settings.Orientation)).Value
            );

    private static AutomationCanvasPosition DisplayPosition(
        AutomationCanvasPosition position,
        AutomationFlowOrientation orientation
    ) =>
        orientation == AutomationFlowOrientation.Horizontal
            ? position
            : new(position.Y, position.X);

    private string NodeStyle(AutomationEditorNode node)
    {
        var display = DisplayPosition(node.Position, Settings.Orientation);
        return FormattableString.Invariant(
            $"--automation-node-x:{display.X.Value};--automation-node-y:{display.Y.Value}"
        );
    }

    private static string PortStyle(int index, int count)
    {
        var offset = (index - ((count - 1) / 2d)) * 28;
        return FormattableString.Invariant($"--automation-port-offset:{offset}");
    }

    private IReadOnlyList<AutomationGraphError> ErrorsFor(AutomationNodeId nodeId) =>
        Errors.Where(error => error.NodeId == nodeId).ToArray();

    private AutomationSampleNodeOutcome? OutcomeFor(AutomationNodeId nodeId) =>
        SampleOutcomes.FirstOrDefault(outcome => outcome.NodeId == nodeId);

    private string NodeClass(
        AutomationEditorNode node,
        bool invalid,
        AutomationSampleNodeOutcome? outcome
    ) =>
        string.Join(
            ' ',
            new[]
            {
                "automation-node",
                SelectedNodeIds.Contains(node.Id) ? "automation-node--selected" : string.Empty,
                invalid || Disconnected(node.Id) ? "automation-node--invalid" : string.Empty,
                outcome?.State == AutomationNodeRunState.Failed ? "automation-node--failed"
                : outcome is not null ? "automation-node--ran"
                : string.Empty,
            }.Where(static value => value.Length > 0)
        );

    private bool Disconnected(AutomationNodeId nodeId) =>
        Edges.All(edge => edge.SourceNodeId != nodeId && edge.TargetNodeId != nodeId);

    private string EdgeGroupClass(AutomationFlowDraftEdge edge) =>
        string.Join(
            ' ',
            new[]
            {
                "automation-edge-group",
                edge.Kind == AutomationEdgeKind.Flow
                    ? "automation-edge-group--flow"
                    : "automation-edge-group--data",
                BranchToken(edge.SourcePortId),
                AutomationConnections.Issue(edge, Nodes) is null
                    ? string.Empty
                    : "automation-edge-group--invalid",
                SelectedEdgeId == edge.Id ? "automation-edge-group--selected" : string.Empty,
            }.Where(static value => value.Length > 0)
        );

    private string? BranchLabel(AutomationFlowDraftEdge edge) =>
        edge.SourcePortId.Value switch
        {
            "yes" => "Yes",
            "no" => "No",
            _ => null,
        };

    private static string BranchToken(AutomationPortId portId) =>
        portId.Value switch
        {
            "yes" => "automation-branch--true",
            "no" => "automation-branch--false",
            _ => "automation-branch--default",
        };

    private static string NodeSummary(AutomationEditorNode node) =>
        node.Definition.Kind switch
        {
            AutomationNodeKind.Source => "Starts this flow.",
            AutomationNodeKind.Control => "Selects the next branch.",
            AutomationNodeKind.Action => "Runs this action.",
            _ => "Runs this node.",
        };

    private static string KindToken(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "source",
            AutomationNodeKind.Value => "value",
            AutomationNodeKind.Transform => "transform",
            AutomationNodeKind.Control => "control",
            AutomationNodeKind.Action => "action",
            _ => "node",
        };

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "Trigger",
            AutomationNodeKind.Value => "Value",
            AutomationNodeKind.Transform => "CEL Transform",
            AutomationNodeKind.Control => "Condition",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };

    private static string PortClass(AutomationPortMetadata port, string direction) =>
        $"automation-port automation-port--{direction} automation-port--{(port.ValueType == AutomationPortValueType.Flow ? "flow" : "data")}";

    private static string PortDisplay(AutomationPortMetadata port) =>
        port.ValueType == AutomationPortValueType.Flow
            ? port.Name
            : $"{port.Name} · {AutomationConnections.TypeLabel(port)}";

    private string InputOccupied(AutomationNodeId nodeId, AutomationPortId portId) =>
        Edges.Any(edge =>
            edge.Kind == AutomationEdgeKind.Data
            && edge.TargetNodeId == nodeId
            && edge.TargetPortId == portId
        )
            ? "true"
            : "false";

    private string NodeAccessibleLabel(AutomationEditorNode node)
    {
        var ports = node
            .Definition.Inputs.Select(port => $"{PortDisplay(port)} input")
            .Concat(node.Definition.Outputs.Select(port => $"{PortDisplay(port)} output"));
        return $"Select {node.EffectiveName}. {string.Join(". ", ports)}";
    }

    private static string TypeBadges(AutomationEditorNode node) =>
        string.Join(
            " · ",
            node.Definition.Inputs.Concat(node.Definition.Outputs)
                .Where(static port => port.ValueType != AutomationPortValueType.Flow)
                .Select(AutomationConnections.TypeLabel)
                .Distinct(StringComparer.Ordinal)
        );

    private string EdgeAccessibleLabel(AutomationFlowDraftEdge edge)
    {
        var source = Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var target = Nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        var sourceName = source?.EffectiveName ?? "Unavailable source";
        var targetName = target?.EffectiveName ?? "Unavailable target";
        var kind = edge.Kind == AutomationEdgeKind.Flow ? "Flow" : "Data";
        var issue = AutomationConnections.Issue(edge, Nodes);
        return issue is null
            ? $"{kind} connection from {sourceName} to {targetName}"
            : $"{kind} connection from {sourceName} to {targetName}. Needs repair. {issue}";
    }

    private static string EdgeMarker(AutomationFlowDraftEdge edge) =>
        edge.Kind == AutomationEdgeKind.Flow
            ? "url(#automation-flow-marker)"
            : "url(#automation-data-marker)";

    private IReadOnlyList<AutomationRetainedPort> RetainedPorts(
        AutomationEditorNode node,
        bool input
    )
    {
        var declared = (input ? node.Definition.Inputs : node.Definition.Outputs)
            .Select(static port => port.Id)
            .ToHashSet();
        var retained = Edges
            .Where(edge => input ? edge.TargetNodeId == node.Id : edge.SourceNodeId == node.Id)
            .Select(edge => input ? edge.TargetPortId : edge.SourcePortId)
            .Where(portId => !declared.Contains(portId))
            .Distinct()
            .OrderBy(static portId => portId.Value, StringComparer.Ordinal)
            .ToArray();
        var count = declared.Count + retained.Length;
        return retained
            .Select(
                (portId, index) => new AutomationRetainedPort(portId, declared.Count + index, count)
            )
            .ToArray();
    }

    private string RenderSignature() =>
        string.Join(
            '|',
            Settings.Orientation,
            Settings.EdgeStyle,
            ViewportKey,
            string.Join(
                ';',
                Nodes.Select(node =>
                    $"{node.Id.Value:N}:{node.Position.X.Value}:{node.Position.Y.Value}:{node.Definition.Inputs.Length}:{node.Definition.Outputs.Length}:{OutcomeFor(node.Id)?.OutcomeCode}"
                )
            ),
            string.Join(
                ';',
                Edges.Select(edge =>
                    $"{edge.Id:N}:{edge.Kind}:{edge.SourceNodeId.Value:N}:{edge.SourcePortId.Value}:{edge.TargetNodeId.Value:N}:{edge.TargetPortId.Value}"
                )
            )
        );

    private static string OutcomeLabel(AutomationSampleNodeOutcome outcome) =>
        outcome.OutcomeCode switch
        {
            "source-received" => "Sample received",
            "condition-true" => "Yes branch",
            "condition-false" => "No branch",
            "delay-skipped" => "Delay skipped",
            "action-simulated" => "Action not sent",
            _ => outcome.State == AutomationNodeRunState.Failed
                ? "Sample failed"
                : outcome.OutcomeCode,
        };

    private static string OrientationToken(AutomationFlowOrientation orientation) =>
        orientation == AutomationFlowOrientation.Vertical ? "vertical" : "horizontal";

    private static string EdgeStyleToken(AutomationEdgeStyle edgeStyle) =>
        edgeStyle == AutomationEdgeStyle.Smooth ? "smooth" : "angular";

    private static int Snapped(int value) => Math.Max(0, (int)Math.Round(value / 24d) * 24);

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

internal sealed record AutomationRetainedPort(AutomationPortId PortId, int Index, int Count);
