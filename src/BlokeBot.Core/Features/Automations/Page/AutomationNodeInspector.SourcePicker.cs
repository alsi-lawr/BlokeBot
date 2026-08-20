using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
    private void OpenSourcePicker(AutomationPortMetadata input, Guid? edgeId)
    {
        _pickerInputId = input.Id;
        _pickerEdgeId = edgeId;
        var choices = SourceChoices();
        _selectedSource =
            choices.FirstOrDefault(static choice => choice.Compatibility.IsCompatible)
            ?? choices.FirstOrDefault();
        _pickerNeedsFocus = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (
            _pickerNeedsFocus
            && _selectedSource is { } source
            && _sourceReferences.TryGetValue(source, out var reference)
        )
        {
            _pickerNeedsFocus = false;
            await reference.FocusAsync();
        }
    }

    private async Task CancelSourcePickerAsync()
    {
        _pickerInputId = null;
        _pickerEdgeId = null;
        _selectedSource = null;
        await InvokeAsync(StateHasChanged);
        await _pickerOpener.FocusAsync();
    }

    private Task HandlePickerKeyAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CancelSourcePickerAsync() : Task.CompletedTask;

    private void SelectSource(AutomationSourceChoice choice) => _selectedSource = choice;

    private async Task HandleSourceChoiceKeyAsync(
        AutomationSourceChoice choice,
        KeyboardEventArgs args
    )
    {
        var choices = SourceChoices().ToArray();
        var index = Array.IndexOf(choices, choice);
        var target = args.Key switch
        {
            "ArrowDown" or "ArrowRight" => (index + 1) % choices.Length,
            "ArrowUp" or "ArrowLeft" => (index + choices.Length - 1) % choices.Length,
            "Home" => 0,
            "End" => choices.Length - 1,
            _ => -1,
        };
        if (index < 0 || target < 0)
        {
            return;
        }

        _selectedSource = choices[target];
        await InvokeAsync(StateHasChanged);
        if (_sourceReferences.TryGetValue(_selectedSource, out var reference))
        {
            await reference.FocusAsync();
        }
    }

    private async Task ConnectSelectedSourceAsync()
    {
        if (
            Node is null
            || _pickerInputId is not { } inputId
            || _selectedSource is not { Compatibility.IsCompatible: true } source
        )
        {
            return;
        }
        var request = new AutomationConnectionRequest(
            source.Node.Id,
            source.Port.Id,
            Node.Id,
            inputId
        );
        if (_pickerEdgeId is { } edgeId)
        {
            await Repair.InvokeAsync(new(edgeId, request));
        }
        else
        {
            await Connect.InvokeAsync(request);
        }
        await CancelSourcePickerAsync();
    }

    private IReadOnlyList<AutomationSourceChoice> SourceChoices() =>
        Node is not { } node
        || _pickerInputId is not { } inputId
        || node.Definition.Inputs.FirstOrDefault(port => port.Id == inputId) is not { } input
            ? []
            : Nodes
                .Where(candidate => candidate.Id != node.Id)
                .SelectMany(candidate =>
                    candidate
                        .Definition.Outputs.Where(static port =>
                            port.ValueType != AutomationPortValueType.Flow
                        )
                        .Select(port => new AutomationSourceChoice(
                            candidate,
                            port,
                            AutomationConnections.Compatibility(candidate, port, Node, input)
                        ))
                )
                .OrderByDescending(static choice => choice.Compatibility.IsCompatible)
                .ThenBy(
                    static choice => choice.Node.EffectiveName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ThenBy(static choice => choice.Port.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private AutomationFlowDraftEdge? IncomingDataEdge(AutomationPortId portId) =>
        Node is null
            ? null
            : Edges.FirstOrDefault(edge =>
                edge.Kind == AutomationEdgeKind.Data
                && edge.TargetNodeId == Node.Id
                && edge.TargetPortId == portId
            );

    private string ConnectedSourceLabel(AutomationFlowDraftEdge edge)
    {
        var source = Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var port = source?.Definition.Outputs.FirstOrDefault(candidate =>
            candidate.Id == edge.SourcePortId
        );
        return $"{source?.EffectiveName ?? "Unavailable node"} · {port?.Name ?? "Unavailable port"}";
    }

    private string ConnectedSourceType(AutomationFlowDraftEdge edge)
    {
        var source = Nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var port = source?.Definition.Outputs.FirstOrDefault(candidate =>
            candidate.Id == edge.SourcePortId
        );
        return port is null ? "Unknown type" : AutomationConnections.TypeLabel(port);
    }
}
