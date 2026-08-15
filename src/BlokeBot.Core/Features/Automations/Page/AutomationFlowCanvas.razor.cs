using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationFlowCanvas
{
    private const int _gridSize = 24;
    private const int _nodeWidth = 168;
    private const int _nodeHeight = 116;
    private ElementReference _root;
    private IJSObjectReference? _module;
    private DotNetObjectReference<AutomationFlowCanvas>? _self;

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationEditorNode> Nodes { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationFlowDraftEdge> Edges { get; set; } = [];

    [Parameter]
    public AutomationNodeId? SelectedNodeId { get; set; }

    [Parameter]
    public IReadOnlyList<AutomationGraphError> Errors { get; set; } = [];

    [Parameter]
    public IReadOnlyList<AutomationSampleNodeOutcome> SampleOutcomes { get; set; } = [];

    [Parameter]
    public EventCallback<AutomationNodeId> SelectNode { get; set; }

    [Parameter]
    public EventCallback<AutomationNodeMoveRequest> MoveNode { get; set; }

    [Parameter]
    public EventCallback<AutomationNodeId> DeleteNode { get; set; }

    private string _connectionSummary =>
        Errors.Any(error => error.Code.Contains("port", StringComparison.Ordinal))
            ? "Connection needs attention"
            : "Typed connections";

    private int _mobileCanvasHeight => Math.Max(496, 72 + (Nodes.Count * 144));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _module = await _js.InvokeAsync<IJSObjectReference>(
            "import",
            "./Features/Automations/Page/AutomationFlowCanvas.razor.js"
        );
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("initialize", _root, _self);
    }

    [JSInvokable]
    public Task MoveNodeFromCanvasAsync(string nodeId, int x, int y) =>
        Guid.TryParse(nodeId, out var parsed)
            ? MoveNode.InvokeAsync(new(new(parsed), Snapped(x), Snapped(y)))
            : Task.CompletedTask;

    private async Task HandleNodeKeyAsync(AutomationEditorNode node, KeyboardEventArgs args)
    {
        if (args.Key is "Delete" or "Backspace")
        {
            await DeleteNode.InvokeAsync(node.Id);
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

        await MoveNode.InvokeAsync(
            new(
                node.Id,
                Math.Max(0, node.Position.X.Value + movement.Item1),
                Math.Max(0, node.Position.Y.Value + movement.Item2)
            )
        );
    }

    private string DesktopPath(AutomationFlowDraftEdge edge)
    {
        var source = Nodes.Single(node => node.Id == edge.SourceNodeId);
        var target = Nodes.Single(node => node.Id == edge.TargetNodeId);
        var sourcePorts = source.Definition.Outputs.Where(IsFlowPort).ToArray();
        var outputIndex = Array.FindIndex(sourcePorts, port => port.Id == edge.SourcePortId);
        var sourceY = source.Position.Y.Value + PortOffset(outputIndex, sourcePorts.Length);
        var sourceX = source.Position.X.Value + _nodeWidth;
        var targetX = target.Position.X.Value;
        var targetY = target.Position.Y.Value + (_nodeHeight / 2);
        var middleX = sourceX + Math.Max(24, (targetX - sourceX) / 2);
        return FormattableString.Invariant(
            $"M {sourceX} {sourceY} H {middleX} V {targetY} H {targetX}"
        );
    }

    private string MobilePath(AutomationFlowDraftEdge edge)
    {
        var sourceIndex = Nodes.ToList().FindIndex(node => node.Id == edge.SourceNodeId);
        var targetIndex = Nodes.ToList().FindIndex(node => node.Id == edge.TargetNodeId);
        var sourceY = 72 + (sourceIndex * 144) + _nodeHeight;
        var targetY = 72 + (targetIndex * 144);
        var branchX = 179 + ((targetIndex - sourceIndex) * 16);
        return FormattableString.Invariant(
            $"M 179 {sourceY} V {sourceY + 12} H {branchX} V {targetY - 12} H 179 V {targetY}"
        );
    }

    private static int PortOffset(int index, int count) =>
        count <= 1 ? _nodeHeight / 2 : 44 + (Math.Max(0, index) * 32);

    private string NodeStyle(AutomationEditorNode node)
    {
        var mobileIndex = Nodes.ToList().FindIndex(candidate => candidate.Id == node.Id);
        return FormattableString.Invariant(
            $"--automation-node-x:{node.Position.X.Value};--automation-node-y:{node.Position.Y.Value};--automation-mobile-y:{72 + (mobileIndex * 144)}"
        );
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
                node.Id == SelectedNodeId ? "automation-node--selected" : string.Empty,
                invalid ? "automation-node--invalid" : string.Empty,
                outcome?.State == AutomationNodeRunState.Failed ? "automation-node--failed"
                : outcome is not null ? "automation-node--ran"
                : string.Empty,
            }.Where(static value => value.Length > 0)
        );

    private static string KindToken(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "source",
            AutomationNodeKind.Control => "control",
            AutomationNodeKind.Action => "action",
            _ => "node",
        };

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "Event",
            AutomationNodeKind.Control => "Condition",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };

    private static bool IsFlowPort(AutomationPortMetadata port) =>
        port.ValueType == AutomationPortValueType.Flow;

    private static string ErrorId(AutomationNodeId nodeId) => $"automation-error-{nodeId.Value:N}";

    private static string OutcomeLabel(AutomationSampleNodeOutcome outcome) =>
        outcome.OutcomeCode switch
        {
            "source-received" => "Sample event received",
            "condition-true" => "Matched sample",
            "condition-false" => "Did not match sample",
            "delay-skipped" => "Delay skipped in sample",
            "action-simulated" => "Simulated — no action sent",
            _ => outcome.State == AutomationNodeRunState.Failed
                ? "Sample failed here"
                : outcome.OutcomeCode,
        };

    private static int Snapped(int value) => Math.Max(0, (int)Math.Round(value / 24d) * 24);

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}

public sealed record AutomationNodeMoveRequest(AutomationNodeId NodeId, int X, int Y);
