namespace BlokeBot.Core.Features.Automations.Page;

internal sealed record AutomationConnectionCompatibility(bool IsCompatible, string Reason)
{
    internal static AutomationConnectionCompatibility Compatible { get; } =
        new(true, "Correct type");
}

internal static class AutomationConnections
{
    internal static AutomationEdgeKind Kind(AutomationPortMetadata output) =>
        output.ValueType == AutomationPortValueType.Flow
            ? AutomationEdgeKind.Flow
            : AutomationEdgeKind.Data;

    internal static AutomationConnectionCompatibility Compatibility(
        AutomationNodeKind sourceKind,
        AutomationPortMetadata output,
        AutomationPortMetadata input
    ) =>
        (output.ValueType, input.ValueType, sourceKind) switch
        {
            (AutomationPortValueType.Flow, AutomationPortValueType.Flow, _) =>
                AutomationConnectionCompatibility.Compatible,
            (AutomationPortValueType.Flow, _, _) => new(
                false,
                "Flow outputs connect only to Flow inputs."
            ),
            (_, _, _)
                when input.ValueType == AutomationPortValueType.Flow
                    || output.ValueType != input.ValueType => new(
                false,
                $"Expected {TypeLabel(input)}. The selected source port supplies {TypeLabel(output)}."
            ),
            (_, _, AutomationNodeKind.Action) => new(
                false,
                "Use Data from a trigger, Value, Transform, or Control node."
            ),
            (_, _, _)
                when output.Nullability == AutomationPortNullability.Nullable
                    && input.Nullability == AutomationPortNullability.NonNullable => new(
                false,
                $"Expected {TypeLabel(input)}. The selected source can be null."
            ),
            (_, _, _)
                when output.Sensitivity == AutomationDataSensitivity.Sensitive
                    && input.Sensitivity == AutomationDataSensitivity.Safe => new(
                false,
                "This input cannot accept Sensitive Data."
            ),
            _ => AutomationConnectionCompatibility.Compatible,
        };

    internal static AutomationConnectionCompatibility Compatibility(
        AutomationEditorNode source,
        AutomationPortMetadata output,
        AutomationEditorNode target,
        AutomationPortMetadata input
    ) =>
        source.Id == target.Id
            ? new(false, "Choose a different node.")
            : Compatibility(source.Definition.Kind, output, input);

    internal static string? Issue(
        AutomationFlowDraftEdge edge,
        IReadOnlyList<AutomationEditorNode> nodes
    )
    {
        var source = nodes.FirstOrDefault(node => node.Id == edge.SourceNodeId);
        var target = nodes.FirstOrDefault(node => node.Id == edge.TargetNodeId);
        if (source is null || target is null)
        {
            return "A saved node is not available.";
        }

        var output = source.Definition.Outputs.FirstOrDefault(port => port.Id == edge.SourcePortId);
        var input = target.Definition.Inputs.FirstOrDefault(port => port.Id == edge.TargetPortId);
        if (output is null || input is null)
        {
            return "A saved port is not available.";
        }
        if (Kind(output) != edge.Kind)
        {
            return edge.Kind == AutomationEdgeKind.Flow
                ? "The selected ports do not carry Flow."
                : "The selected ports do not carry Data.";
        }

        var compatibility = Compatibility(source, output, target, input);
        return compatibility.IsCompatible ? null : compatibility.Reason;
    }

    internal static string TypeLabel(AutomationPortMetadata port) =>
        $"{port.ValueType}{(port.Nullability == AutomationPortNullability.Nullable ? "?" : string.Empty)}";
}
