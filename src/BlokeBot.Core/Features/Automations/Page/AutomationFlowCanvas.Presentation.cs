namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationFlowCanvas
{
    private IReadOnlyList<string> ErrorsFor(AutomationEditorNode node) =>
        AutomationValidationPresentation.NodeIssues(node, Nodes, Edges, Errors);

    private AutomationScenarioNodeOutcome? OutcomeFor(AutomationNodeId nodeId) =>
        SampleOutcomes.FirstOrDefault(outcome => outcome.NodeId == nodeId);

    private string NodeClass(
        AutomationEditorNode node,
        bool invalid,
        AutomationScenarioNodeOutcome? outcome
    ) =>
        string.Join(
            ' ',
            new[]
            {
                "automation-node",
                SelectedNodeIds.Contains(node.Id) ? "automation-node--selected" : string.Empty,
                DisclosedNodeId == node.Id ? "automation-node--disclosed" : string.Empty,
                invalid ? "automation-node--invalid" : string.Empty,
                outcome?.State
                    is AutomationNodeRunState.Failed
                        or AutomationNodeRunState.ContinuedAfterFailure
                        or AutomationNodeRunState.Invalidated
                    ? "automation-node--failed"
                : outcome is not null ? "automation-node--ran"
                : string.Empty,
            }.Where(static value => value.Length > 0)
        );

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
            AutomationNodeKind.Control => "Control",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };

    private static string PortClass(AutomationPortMetadata port, string direction) =>
        $"automation-port automation-port--{direction} automation-port--{(port.ValueType == AutomationPortValueType.Flow ? "flow" : "data")}";

    private static string PortMarkerShape(AutomationPortMetadata port) =>
        port.ValueType == AutomationPortValueType.Flow ? "circle" : "diamond";

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

    private string NodeAccessibleLabel(
        AutomationEditorNode node,
        bool needsRepair,
        AutomationScenarioNodeOutcome? outcome
    )
    {
        var ports = node
            .Definition.Inputs.Select(port => $"{PortDisplay(port)} input")
            .Concat(node.Definition.Outputs.Select(port => $"{PortDisplay(port)} output"));
        return $"Select {node.EffectiveName}. {KindLabel(node.Definition.Kind)}. {node.Definition.Display.Name} icon. {NodeStatus(needsRepair, outcome)}. Ports: {string.Join(". ", ports)}";
    }

    private string NodeStatus(bool needsRepair, AutomationScenarioNodeOutcome? outcome) =>
        AutomationNodePresentation.Status(needsRepair, Validated, outcome);

    private string EdgeAccessibleLabel(AutomationFlowDraftEdge edge) =>
        AutomationConnections.Label(edge, Nodes);

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
            DisclosedNodeId?.Value,
            DisclosureGeneration,
            string.Join(
                ';',
                Nodes.Select(node =>
                    $"{node.Id.Value:N}:{node.Position.X.Value}:{node.Position.Y.Value}:{string.Join(',', node.Definition.Inputs.Select(static port => port.Id.Value))}>{string.Join(',', node.Definition.Outputs.Select(static port => port.Id.Value))}:{OutcomeFor(node.Id)?.OutcomeCode}"
                )
            ),
            string.Join(
                ';',
                Edges.Select(edge =>
                    $"{edge.Id:N}:{edge.Kind}:{edge.SourceNodeId.Value:N}:{edge.SourcePortId.Value}:{edge.TargetNodeId.Value:N}:{edge.TargetPortId.Value}"
                )
            )
        );

    private static string OrientationToken(AutomationFlowOrientation orientation) =>
        orientation == AutomationFlowOrientation.Vertical ? "vertical" : "horizontal";

    private static string EdgeStyleToken(AutomationEdgeStyle edgeStyle) =>
        edgeStyle == AutomationEdgeStyle.Smooth ? "smooth" : "angular";

    private static int Snapped(int value) => Math.Max(0, (int)Math.Round(value / 24d) * 24);
}
