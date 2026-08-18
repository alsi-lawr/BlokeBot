using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
    private AutomationPortId? _pickerInputId;
    private Guid? _pickerEdgeId;
    private AutomationSourceChoice? _selectedSource;
    private ElementReference _pickerOpener;
    private readonly Dictionary<AutomationSourceChoice, ElementReference> _sourceReferences = [];
    private bool _pickerNeedsFocus;

    [Parameter]
    public AutomationEditorNode? Node { get; set; }

    [Parameter]
    public bool MobileOpen { get; set; }

    [Parameter]
    public IReadOnlyList<AutomationEditorNode> Nodes { get; set; } = [];

    [Parameter]
    public IReadOnlyList<AutomationFlowDraftEdge> Edges { get; set; } = [];

    [Parameter]
    public IReadOnlyList<AutomationGraphError> Errors { get; set; } = [];

    [Parameter]
    public IReadOnlyDictionary<
        AutomationReferenceKind,
        IReadOnlyList<AutomationReferenceChoice>
    > ReferenceChoices { get; set; } =
        new Dictionary<AutomationReferenceKind, IReadOnlyList<AutomationReferenceChoice>>();

    [Parameter]
    public EventCallback Changed { get; set; }

    [Parameter]
    public EventCallback ClearSelection { get; set; }

    [Parameter]
    public EventCallback<AutomationConnectionRequest> Connect { get; set; }

    [Parameter]
    public EventCallback<AutomationRepairConnectionRequest> Repair { get; set; }

    [Parameter]
    public EventCallback<Guid> DeleteEdge { get; set; }

    [Parameter]
    public EventCallback<AutomationNodeId> DeleteNode { get; set; }

    private static IReadOnlyList<AutomationPortValueType> _inputTypes { get; } =
        Enum.GetValues<AutomationPortValueType>()
            .Where(static type => type != AutomationPortValueType.Flow)
            .ToArray();
    private static IReadOnlyList<AutomationPortValueType> _outputTypes { get; } =
    [
        AutomationPortValueType.Text,
        AutomationPortValueType.Number,
        AutomationPortValueType.Boolean,
        AutomationPortValueType.Timestamp,
    ];

    private Task ChangedAsync() => Changed.InvokeAsync();

    private async Task SetValueAsync(AutomationConfigurationFieldId fieldId, object? value)
    {
        Node?.SetValue(fieldId, value?.ToString() ?? string.Empty);
        await Changed.InvokeAsync();
    }

    private async Task SetExpressionAsync(AutomationConfigurationFieldId fieldId, object? value)
    {
        Node?.SetExpression(
            fieldId,
            new(AutomationExpressionLanguage.CurrentVersion, value?.ToString() ?? string.Empty)
        );
        await Changed.InvokeAsync();
    }

    private async Task SetBindingModeAsync(
        AutomationConfigurationFieldId fieldId,
        AutomationInputBindingMode mode
    )
    {
        Node?.SetBindingMode(fieldId, mode);
        await Changed.InvokeAsync();
    }

    private async Task SetDisplayAliasAsync(object? value)
    {
        Node?.SetDisplayAlias(value?.ToString());
        await Changed.InvokeAsync();
    }

    private async Task ConnectAsync(AutomationPortMetadata output, object? value)
    {
        if (Node is null || !Guid.TryParse(value?.ToString(), out var targetId))
        {
            return;
        }
        var target = Nodes.Single(candidate => candidate.Id.Value == targetId);
        var input = target.Definition.Inputs.Single(port =>
            AutomationConnections.Compatibility(Node, output, target, port).IsCompatible
        );
        await Connect.InvokeAsync(new(Node.Id, output.Id, target.Id, input.Id));
    }

    private void OpenSourcePicker(AutomationPortMetadata input, Guid? edgeId)
    {
        _pickerInputId = input.Id;
        _pickerEdgeId = edgeId;
        _selectedSource = SourceChoices()
            .FirstOrDefault(static choice => choice.Compatibility.IsCompatible);
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

    private IReadOnlyList<AutomationSourceChoice> SourceChoices()
    {
        if (
            Node is null
            || _pickerInputId is not { } inputId
            || Node.Definition.Inputs.FirstOrDefault(port => port.Id == inputId) is not { } input
        )
        {
            return [];
        }
        var choices = Nodes
            .Where(candidate => candidate.Id != Node.Id)
            .SelectMany(candidate =>
                candidate
                    .Definition.Outputs.Where(static port =>
                        port.ValueType != AutomationPortValueType.Flow
                    )
                    .Select(port => new AutomationSourceChoice(
                        candidate,
                        port,
                        AutomationConnections.Compatibility(candidate, port, Node, input),
                        false
                    ))
            )
            .OrderByDescending(static choice => choice.Compatibility.IsCompatible)
            .ThenBy(static choice => choice.Node.EffectiveName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static choice => choice.Port.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var first = Array.FindIndex(choices, static choice => choice.Compatibility.IsCompatible);
        return choices
            .Select((choice, index) => choice with { FirstCompatible = index == first })
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

    private IReadOnlyList<AutomationGraphError> _genericErrors =>
        Node is null
            ? []
            : Errors
                .Where(error =>
                    error.NodeId == Node.Id && error.FieldId is null && error.PortId is null
                )
                .ToArray();

    private IReadOnlyList<AutomationGraphError> FieldErrors(
        AutomationConfigurationFieldId fieldId
    ) =>
        Node is null
            ? []
            : Errors.Where(error => error.NodeId == Node.Id && error.FieldId == fieldId).ToArray();

    private IReadOnlyList<AutomationEditorNode> CompatibleTargets(AutomationPortMetadata output) =>
        Nodes
            .Where(candidate =>
                candidate.Id != Node?.Id
                && candidate.Definition.Inputs.Any(input =>
                    Node is not null
                    && AutomationConnections
                        .Compatibility(Node, output, candidate, input)
                        .IsCompatible
                )
                && !Edges.Any(edge =>
                    edge.SourceNodeId == Node?.Id
                    && edge.SourcePortId == output.Id
                    && edge.TargetNodeId == candidate.Id
                )
            )
            .OrderBy(static candidate => candidate.EffectiveName)
            .ToArray();

    private IReadOnlyList<AutomationReferenceChoice> ChoicesFor(
        AutomationConfigurationFieldMetadata field
    ) =>
        field.FieldType switch
        {
            AutomationConfigurationFieldType.Choice choice => choice
                .Values.Select(value => new AutomationReferenceChoice(value, ChoiceLabel(value)))
                .ToArray(),
            AutomationConfigurationFieldType.Reference reference
                when ReferenceChoices.TryGetValue(reference.ReferenceKind, out var choices) =>
                choices,
            _ => [],
        };

    private async Task AddTransformInputAsync()
    {
        Node?.AddTransformInput();
        await Changed.InvokeAsync();
    }

    private async Task AddTransformOutputAsync()
    {
        Node?.AddTransformOutput();
        await Changed.InvokeAsync();
    }

    private async Task RemoveTransformInputAsync(AutomationPortId portId)
    {
        Node?.RemoveTransformInput(portId);
        await Changed.InvokeAsync();
    }

    private async Task RemoveTransformOutputAsync(AutomationPortId portId)
    {
        Node?.RemoveTransformOutput(portId);
        await Changed.InvokeAsync();
    }

    private async Task UpdateTransformInputAsync(
        AutomationCelTransformInput input,
        string? displayName = null,
        string? valueType = null,
        string? nullability = null
    )
    {
        if (Node is null)
        {
            return;
        }
        _ = Enum.TryParse(valueType, out AutomationPortValueType parsedType);
        _ = Enum.TryParse(nullability, out AutomationPortNullability parsedNullability);
        Node.UpdateTransformInput(
            input.PortId,
            displayName ?? input.DisplayName,
            valueType is null ? input.ValueType : parsedType,
            nullability is null ? input.Nullability : parsedNullability
        );
        await Changed.InvokeAsync();
    }

    private async Task UpdateTransformOutputAsync(
        AutomationCelTransformOutput output,
        string? displayName = null,
        string? valueType = null,
        string? nullability = null,
        string? source = null
    )
    {
        if (Node is null)
        {
            return;
        }
        _ = Enum.TryParse(valueType, out AutomationPortValueType parsedType);
        _ = Enum.TryParse(nullability, out AutomationPortNullability parsedNullability);
        Node.UpdateTransformOutput(
            output.PortId,
            displayName ?? output.DisplayName,
            valueType is null ? output.ValueType : parsedType,
            nullability is null ? output.Nullability : parsedNullability,
            source ?? output.Source
        );
        await Changed.InvokeAsync();
    }

    private static string FixedInputType(AutomationPortMetadata input) =>
        input.ValueType == AutomationPortValueType.Number ? "number"
        : input.ValueType == AutomationPortValueType.Timestamp ? "datetime-local"
        : "text";

    private static string GenericInputType(AutomationConfigurationFieldMetadata field) =>
        field.FieldType
            is AutomationConfigurationFieldType.Number
                or AutomationConfigurationFieldType.Duration
            ? "number"
            : "text";

    private static string FieldId(
        AutomationNodeId nodeId,
        AutomationConfigurationFieldId fieldId
    ) => $"automation-{nodeId.Value:N}-{fieldId.Value}";

    private static string DeclarationFieldId(AutomationPortId portId, string suffix) =>
        $"automation-declaration-{portId.Value}-{suffix}";

    private static string ConnectionId(AutomationNodeId nodeId, AutomationPortId portId) =>
        $"automation-connection-{nodeId.Value:N}-{portId.Value}";

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "trigger",
            AutomationNodeKind.Value => "value",
            AutomationNodeKind.Transform => "transform",
            AutomationNodeKind.Control => "control",
            AutomationNodeKind.Action => "action",
            _ => "node",
        };

    private static string ChoiceLabel(string value) =>
        string.Join(
            ' ',
            value
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(static word => char.ToUpperInvariant(word[0]) + word[1..])
        );

    private static int? MaximumLength(AutomationConfigurationFieldMetadata field) =>
        field.FieldType is AutomationConfigurationFieldType.Text text ? text.MaximumLength : null;

    private static bool IsFlowPort(AutomationPortMetadata port) =>
        port.ValueType == AutomationPortValueType.Flow;

    private static bool IsDataPort(AutomationPortMetadata port) =>
        port.ValueType != AutomationPortValueType.Flow && port.BindingFieldId is not null;
}

public sealed record AutomationConnectionRequest(
    AutomationNodeId SourceNodeId,
    AutomationPortId SourcePortId,
    AutomationNodeId TargetNodeId,
    AutomationPortId TargetPortId
);

public sealed record AutomationRepairConnectionRequest(
    Guid EdgeId,
    AutomationConnectionRequest Connection
);

internal sealed record AutomationSourceChoice(
    AutomationEditorNode Node,
    AutomationPortMetadata Port,
    AutomationConnectionCompatibility Compatibility,
    bool FirstCompatible
);
