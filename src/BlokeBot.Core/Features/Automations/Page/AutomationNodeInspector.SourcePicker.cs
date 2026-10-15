using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
    private void OpenSourcePicker(AutomationPortMetadata input, Guid? edgeId)
    {
        _pickerNodeId = Node?.Id;
        _addingTransformInput = false;
        _pickerInputId = input.Id;
        _pickerEdgeId = edgeId;
        _pickerOpener = _sourceOpeners.GetValueOrDefault(input.Id);
        _sourcePickerError = null;
        var edge = edgeId is { } id ? Edges.FirstOrDefault(edge => edge.Id == id) : null;
        var choices = SourceChoices();
        _selectedSource = edge is null
            ? choices.FirstOrDefault(static choice => choice.Compatibility.IsCompatible)
                ?? choices.FirstOrDefault()
            : choices.FirstOrDefault(choice =>
                choice.Node.Id == edge.SourceNodeId && choice.Port.Id == edge.SourcePortId
            );
        _pickerNeedsFocus = true;
    }

    private void OpenTransformInputSourcePicker()
    {
        _pickerNodeId = Node?.Id;
        _addingTransformInput = true;
        _pickerInputId = null;
        _pickerEdgeId = null;
        _pickerOpener = _addInputOpener;
        _sourcePickerError = null;
        _newInputNameEdited = false;
        _newInputIdentifierEdited = false;
        _selectedSource = null;
        if (
            SourceChoices().FirstOrDefault(static choice => choice.Compatibility.IsCompatible) is
            { } first
        )
        {
            SelectSource(first);
        }
        _pickerNeedsFocus = true;
    }

    private void CloseSourcePicker()
    {
        _pickerNodeId = null;
        _pickerInputId = null;
        _pickerEdgeId = null;
        _addingTransformInput = false;
        _selectedSource = null;
        _sourceReferences.Clear();
        _sourcePickerError = null;
    }

    private string? _newInputDiagnostic =>
        _addingTransformInput
            ? Node?.TransformInputNameDiagnostic(_newInputName, _newInputIdentifier)
            : null;

    private void SetNewInputName(ChangeEventArgs args)
    {
        _newInputName = args.Value?.ToString() ?? string.Empty;
        _newInputNameEdited = true;
    }

    private void SetNewInputIdentifier(ChangeEventArgs args)
    {
        _newInputIdentifier = args.Value?.ToString() ?? string.Empty;
        _newInputIdentifierEdited = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pickerNeedsFocus)
        {
            _pickerNeedsFocus = false;
            var reference =
                _selectedSource is { } source
                && _sourceReferences.TryGetValue(source, out var choiceReference)
                    ? choiceReference
                    : _pickerDialog;
            await reference.FocusAsync();
        }
    }

    private async Task CancelSourcePickerAsync()
    {
        CloseSourcePicker();
        await InvokeAsync(StateHasChanged);
        await _pickerOpener.FocusAsync();
    }

    private Task HandlePickerKeyAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" ? CancelSourcePickerAsync() : Task.CompletedTask;

    private void SelectSource(AutomationSourceChoice choice)
    {
        _selectedSource = choice;
        _sourcePickerError = null;
        if (!_addingTransformInput)
        {
            return;
        }
        if (!_newInputNameEdited)
        {
            _newInputName = choice.Port.Name;
        }
        if (!_newInputIdentifierEdited)
        {
            _newInputIdentifier = Node!.SuggestedTransformInputIdentifier(choice.Port);
        }
    }

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

        SelectSource(choices[target]);
        await InvokeAsync(StateHasChanged);
        if (_sourceReferences.TryGetValue(choices[target], out var reference))
        {
            await reference.FocusAsync();
        }
    }

    private async Task ConnectSelectedSourceAsync()
    {
        if (Node is null || Node.Id != _pickerNodeId || _selectedSource is not { } selected)
        {
            return;
        }
        var source = SourceChoices()
            .FirstOrDefault(choice =>
                choice.Node.Id == selected.Node.Id && choice.Port == selected.Port
            );
        if (source?.Compatibility.IsCompatible != true)
        {
            _sourcePickerError =
                "This source changed or became unavailable. Choose a current source.";
            return;
        }
        if (_addingTransformInput)
        {
            if (_newInputDiagnostic is not null)
            {
                return;
            }
            await AddInputFromSource.InvokeAsync(
                new(source.Node.Id, source.Port, Node.Id, _newInputName, _newInputIdentifier)
            );
        }
        else if (_pickerInputId is { } inputId)
        {
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
        }
        await CancelSourcePickerAsync();
    }

    private IReadOnlyList<AutomationSourceChoice> SourceChoices()
    {
        if (Node is not { } node || node.Id != _pickerNodeId)
        {
            return [];
        }
        var input = _pickerInputId is { } inputId
            ? node.Definition.Inputs.FirstOrDefault(port => port.Id == inputId)
            : null;
        return !_addingTransformInput && input is null
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
                            AutomationConnections.Compatibility(
                                candidate,
                                port,
                                node,
                                _addingTransformInput
                                    ? AutomationEditorNode.SourceDerivedTransformInput(port)
                                    : input!
                            )
                        ))
                )
                .OrderByDescending(static choice => choice.Compatibility.IsCompatible)
                .ThenBy(
                    static choice => choice.Node.EffectiveName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ThenBy(static choice => choice.Port.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

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
