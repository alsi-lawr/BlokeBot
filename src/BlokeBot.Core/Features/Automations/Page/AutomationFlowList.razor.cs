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
    public IReadOnlyList<AutomationSampleNodeOutcome> SampleOutcomes { get; set; } = [];

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

    private IReadOnlyList<AutomationEditorNode> _orderedNodes =>
        Nodes
            .OrderBy(node => node.Id == SelectedNodeId ? 0 : 1)
            .ThenBy(static node => node.Position.Y.Value)
            .ThenBy(static node => node.Position.X.Value)
            .ThenBy(static node => node.EffectiveName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string Incoming(AutomationEditorNode node) =>
        Names(
            Edges
                .Where(edge => edge.TargetNodeId == node.Id)
                .Select(edge =>
                    Nodes
                        .FirstOrDefault(candidate => candidate.Id == edge.SourceNodeId)
                        ?.EffectiveName
                )
        );

    private string Outgoing(AutomationEditorNode node) =>
        Names(
            Edges
                .Where(edge => edge.SourceNodeId == node.Id)
                .Select(edge =>
                    Nodes
                        .FirstOrDefault(candidate => candidate.Id == edge.TargetNodeId)
                        ?.EffectiveName
                )
        );

    private static string Names(IEnumerable<string?> names)
    {
        var values = names
            .Where(static name => name is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? "None" : string.Join(", ", values);
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
        var output = source?.Definition.Outputs.FirstOrDefault(port =>
            port.Id == edge.SourcePortId
        );
        return $"{source?.EffectiveName ?? "Unavailable node"}.{output?.Name ?? "Unavailable port"}";
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
        IncomingDataEdge(node, input) is { } edge ? AutomationConnections.Issue(edge, Nodes)
        : node.Binding(input.BindingFieldId!.Value).Mode == AutomationInputBindingMode.Connected
            ? "Choose a source."
        : null;

    private string? NodeIssue(AutomationEditorNode node) =>
        Edges
            .Where(edge => edge.TargetNodeId == node.Id)
            .Select(edge => AutomationConnections.Issue(edge, Nodes))
            .FirstOrDefault(static issue => issue is not null);

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
