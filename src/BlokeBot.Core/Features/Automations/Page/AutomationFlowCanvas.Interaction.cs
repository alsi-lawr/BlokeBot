using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationFlowCanvas
{
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
            $"--automation-node-x:{display.X.Value};--automation-node-y:{display.Y.Value};--automation-node-port-count:{node.Definition.Inputs.Length + node.Definition.Outputs.Length}"
        );
    }

    private static string PortStyle(int index, int count)
    {
        var offset = (index - ((count - 1) / 2d)) * 28;
        return FormattableString.Invariant($"--automation-port-offset:{offset}");
    }
}
