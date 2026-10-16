using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationFlowList
{
    private readonly Dictionary<AutomationNodeId, ElementReference> _nodeSelectors = [];
    private AutomationNodeId? _focusNodeAfterRender;

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationEditorNode> Nodes { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<AutomationFlowDraftEdge> Edges { get; set; } = [];

    [Parameter]
    public AutomationNodeId? SelectedNodeId { get; set; }

    [Parameter]
    public IReadOnlyList<AutomationGraphError> Errors { get; set; } = [];

    [Parameter]
    public bool Validated { get; set; }

    [Parameter]
    public IReadOnlyList<AutomationScenarioNodeOutcome> SampleOutcomes { get; set; } = [];

    [Parameter]
    public IReadOnlySet<AutomationDefinitionId> UnavailableDefinitionIds { get; set; } =
        new HashSet<AutomationDefinitionId>();

    [Parameter]
    public EventCallback<AutomationNodeId> SelectNode { get; set; }

    [Parameter]
    public EventCallback<AutomationNodeId> DeleteNode { get; set; }

    public void RestoreFocusAfterRender(AutomationNodeId nodeId) => _focusNodeAfterRender = nodeId;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            _focusNodeAfterRender is not { } nodeId
            || !_nodeSelectors.TryGetValue(nodeId, out var selector)
        )
        {
            return;
        }
        _focusNodeAfterRender = null;
        await selector.FocusAsync();
    }

    private string InputSource(AutomationEditorNode node, AutomationPortMetadata input)
    {
        var binding = node.Binding(input.BindingFieldId!.Value);
        if (binding.Mode == AutomationInputBindingMode.Fixed)
        {
            return "Entered value";
        }
        if (binding.Mode == AutomationInputBindingMode.Expression)
        {
            return binding.Expression?.Source ?? "No expression";
        }
        var edge = IncomingDataEdge(node, input);
        if (edge is null)
        {
            return "No source";
        }
        var source = Nodes.FirstOrDefault(candidate => candidate.Id == edge.SourceNodeId);
        var output = AutomationConnections.PortLabel(
            source?.Definition.Outputs ?? [],
            edge.SourcePortId
        );
        return $"{source?.EffectiveName ?? "Unavailable node"}.{output}";
    }

    private AutomationFlowDraftEdge? IncomingDataEdge(
        AutomationEditorNode node,
        AutomationPortMetadata input
    ) =>
        Edges.FirstOrDefault(edge =>
            edge.Kind == AutomationEdgeKind.Data
            && edge.TargetNodeId == node.Id
            && edge.TargetPortId == input.Id
        );

    private string? InputIssue(AutomationEditorNode node, AutomationPortMetadata input) =>
        AutomationValidationPresentation.InputIssue(node, input, Nodes, Edges, Errors);

    private static bool IsDataPort(AutomationPortMetadata port) =>
        port.ValueType != AutomationPortValueType.Flow && port.BindingFieldId is not null;

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "Trigger",
            AutomationNodeKind.Value => "Value",
            AutomationNodeKind.Transform => "Transform",
            AutomationNodeKind.Control => "Control",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };
}
