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
    public EventCallback<AutomationNodeId> SelectNode { get; set; }

    [Parameter]
    public EventCallback<AutomationNodeId> DeleteNode { get; set; }

    public void RestoreFocusAfterRender(AutomationNodeId nodeId) => _focusNodeAfterRender = nodeId;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            _focusNodeAfterRender is not { } nodeId
            || !Nodes.Any(node => node.Id == nodeId)
            || !_nodeSelectors.TryGetValue(nodeId, out var selector)
        )
        {
            return;
        }

        _focusNodeAfterRender = null;
        await selector.FocusAsync();
    }

    private IReadOnlyList<AutomationEditorNode> _orderedNodes
    {
        get
        {
            if (Nodes.Count == 0)
            {
                return [];
            }

            var incoming = Nodes.ToDictionary(static node => node.Id, static _ => 0);
            foreach (var edge in Edges.Where(edge => incoming.ContainsKey(edge.TargetNodeId)))
            {
                incoming[edge.TargetNodeId]++;
            }

            var ready = new Queue<AutomationNodeId>(
                Nodes
                    .Where(node => incoming[node.Id] == 0)
                    .OrderBy(static node => node.Position.Y.Value)
                    .ThenBy(static node => node.Position.X.Value)
                    .Select(static node => node.Id)
            );
            var ordered = new List<AutomationEditorNode>();
            while (ready.TryDequeue(out var nodeId))
            {
                ordered.Add(Nodes.Single(node => node.Id == nodeId));
                foreach (
                    var target in Edges
                        .Where(edge => edge.SourceNodeId == nodeId)
                        .OrderBy(static edge => edge.SourcePortId.Value)
                        .Select(static edge => edge.TargetNodeId)
                )
                {
                    incoming[target]--;
                    if (incoming[target] == 0)
                    {
                        ready.Enqueue(target);
                    }
                }
            }

            return ordered.Count == Nodes.Count ? ordered : Nodes;
        }
    }

    private string ConnectionLabel(AutomationNodeId nodeId)
    {
        var outgoing = Edges.Count(edge => edge.SourceNodeId == nodeId);
        return outgoing switch
        {
            0 => "End of branch",
            1 => "1 next node",
            _ => $"{outgoing} branches",
        };
    }

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "Event",
            AutomationNodeKind.Control => "Control",
            AutomationNodeKind.Action => "Action",
            _ => "Node",
        };

    private static string OutcomeLabel(AutomationSampleNodeOutcome outcome) =>
        outcome.State == AutomationNodeRunState.Failed ? "Sample failed" : "Sample passed";
}
