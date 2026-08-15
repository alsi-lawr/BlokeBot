using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
    [Parameter]
    public AutomationEditorNode? Node { get; set; }

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
    public EventCallback<Guid> DeleteEdge { get; set; }

    [Parameter]
    public EventCallback<AutomationNodeId> DeleteNode { get; set; }

    private Task ChangedAsync() => Changed.InvokeAsync();

    private async Task SetValueAsync(AutomationConfigurationFieldId fieldId, object? value)
    {
        Node?.SetValue(fieldId, value?.ToString() ?? string.Empty);
        await Changed.InvokeAsync();
    }

    private async Task ConnectAsync(AutomationPortMetadata output, object? value)
    {
        if (Node is null || !Guid.TryParse(value?.ToString(), out var targetId))
        {
            return;
        }

        var target = Nodes.Single(candidate => candidate.Id.Value == targetId);
        var input = target.Definition.Inputs.Single(port => Compatible(output, port));
        await Connect.InvokeAsync(new(Node.Id, output.Id, target.Id, input.Id));
    }

    private IReadOnlyList<AutomationGraphError> _genericErrors =>
        Node is null
            ? []
            : Errors.Where(error => error.NodeId == Node.Id && error.FieldId is null).ToArray();

    private IReadOnlyList<AutomationGraphError> FieldErrors(
        AutomationConfigurationFieldId fieldId
    ) =>
        Node is null
            ? []
            : Errors.Where(error => error.NodeId == Node.Id && error.FieldId == fieldId).ToArray();

    private static string FieldHelp(AutomationConfigurationFieldMetadata field) =>
        field.Description;

    private IReadOnlyList<AutomationEditorNode> CompatibleTargets(AutomationPortMetadata output) =>
        Nodes
            .Where(candidate =>
                candidate.Id != Node?.Id
                && candidate.Definition.Inputs.Any(input => Compatible(output, input))
                && !Edges.Any(edge =>
                    edge.SourceNodeId == Node?.Id
                    && edge.SourcePortId == output.Id
                    && edge.TargetNodeId == candidate.Id
                )
            )
            .OrderBy(static candidate => candidate.Definition.Display.Name)
            .ToArray();

    private static bool Compatible(AutomationPortMetadata output, AutomationPortMetadata input) =>
        output.ValueType == input.ValueType
        && output.Sensitivity == input.Sensitivity
        && output.ValueType == AutomationPortValueType.Flow;

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

    private string NodeLabel(AutomationNodeId nodeId) =>
        Nodes.Single(node => node.Id == nodeId).Definition.Display.Name;

    private string PortLabel(AutomationPortId portId) =>
        Node?.Definition.Outputs.Single(port => port.Id == portId).Name ?? portId.Value;

    private static string FieldId(
        AutomationNodeId nodeId,
        AutomationConfigurationFieldId fieldId
    ) => $"automation-{nodeId.Value:N}-{fieldId.Value}";

    private static string ConnectionId(AutomationNodeId nodeId, AutomationPortId portId) =>
        $"automation-connection-{nodeId.Value:N}-{portId.Value}";

    private static string KindLabel(AutomationNodeKind kind) =>
        kind switch
        {
            AutomationNodeKind.Source => "event",
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

    private static long DurationMinimum(
        AutomationConfigurationFieldMetadata field,
        AutomationConfigurationFieldType.Duration duration
    ) =>
        field.Id.Value.EndsWith("milliseconds", StringComparison.Ordinal)
            ? (long)duration.Minimum.TotalMilliseconds
            : (long)duration.Minimum.TotalSeconds;

    private static long? DurationMaximum(
        AutomationConfigurationFieldMetadata field,
        AutomationConfigurationFieldType.Duration duration
    ) =>
        duration.Maximum is { } maximum
            ? field.Id.Value.EndsWith("milliseconds", StringComparison.Ordinal)
                ? (long)maximum.TotalMilliseconds
                : (long)maximum.TotalSeconds
            : null;

    private static bool IsFlowPort(AutomationPortMetadata port) =>
        port.ValueType == AutomationPortValueType.Flow;
}

public sealed record AutomationConnectionRequest(
    AutomationNodeId SourceNodeId,
    AutomationPortId SourcePortId,
    AutomationNodeId TargetNodeId,
    AutomationPortId TargetPortId
);
