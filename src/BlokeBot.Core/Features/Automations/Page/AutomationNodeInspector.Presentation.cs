namespace BlokeBot.Core.Features.Automations.Page;

public partial class AutomationNodeInspector
{
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

    private static string BindingModeHelpId(
        AutomationPortId portId,
        AutomationInputBindingMode mode
    ) => $"automation-binding-mode-help-{portId.Value}-{mode.ToString().ToLowerInvariant()}";

    private static string BindingModeHelp(AutomationInputBindingMode mode) =>
        mode switch
        {
            AutomationInputBindingMode.Fixed => "Enter a value",
            AutomationInputBindingMode.Connected => "Use another node",
            AutomationInputBindingMode.Expression => "Calculate a value",
        };

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
