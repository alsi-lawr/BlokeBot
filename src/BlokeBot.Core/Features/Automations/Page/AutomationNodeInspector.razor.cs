using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
    private AutomationPortId? _pickerInputId;
    private Guid? _pickerEdgeId;
    private AutomationSourceChoice? _selectedSource;
    private ElementReference _pickerOpener;
    private readonly Dictionary<AutomationSourceChoice, ElementReference> _sourceReferences = [];
    private bool _pickerNeedsFocus;
    private AutomationNodeId? _rejectedRenameNodeId;
    private AutomationPortId? _rejectedRenamePortId;
    private AutomationTransformInputRenameOutcome? _renameFailure;
    private int _renameAttempt;
    private AutomationNodeId? _invalidFixedValueNodeId;
    private AutomationPortId? _invalidFixedValuePortId;
    private int _fixedValueAttempt;

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
        AutomationPortValueType.Array,
        AutomationPortValueType.Map,
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

    private async Task SetComplexFixedValueAsync(AutomationPortId portId, object? value)
    {
        if (Node?.SetComplexFixedValue(portId, value?.ToString() ?? string.Empty) is not true)
        {
            _invalidFixedValueNodeId = Node?.Id;
            _invalidFixedValuePortId = portId;
            _fixedValueAttempt++;
            return;
        }

        _invalidFixedValueNodeId = null;
        _invalidFixedValuePortId = null;
        await Changed.InvokeAsync();
    }

    private string? ComplexFixedValueDiagnostic(AutomationPortId portId) =>
        Node is not null
        && _invalidFixedValueNodeId == Node.Id
        && _invalidFixedValuePortId == portId
            ? "Enter valid JSON for this input type."
            : null;

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
    AutomationConnectionCompatibility Compatibility
);
