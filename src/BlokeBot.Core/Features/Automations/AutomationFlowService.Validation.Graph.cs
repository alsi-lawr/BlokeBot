using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    private static void ValidateBindings(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IReadOnlyDictionary<(AutomationNodeId, AutomationPortId), int> dataIncoming,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (var node in nodes.Values)
        {
            if (!definitions.TryGetValue(node.Id, out var definition))
            {
                continue;
            }

            foreach (
                var input in definition.Inputs.Where(static port =>
                    port.ValueType != AutomationPortValueType.Flow
                )
            )
            {
                if (
                    input.BindingFieldId is not { } fieldId
                    || !node.InputBindings.TryGetValue(fieldId, out var binding)
                )
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-missing",
                            "Choose Fixed, Connected, or Expression for this input.",
                            input.BindingFieldId
                        )
                    );
                    continue;
                }

                var incoming = dataIncoming.GetValueOrDefault((node.Id, input.Id));
                if (binding.Mode == AutomationInputBindingMode.Connected && incoming != 1)
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-connection-missing",
                            "Connect one Data output to this input.",
                            fieldId
                        )
                    );
                }
                else if (binding.Mode != AutomationInputBindingMode.Connected && incoming != 0)
                {
                    errors.Add(
                        new(
                            node.Id,
                            "binding-connection-inactive",
                            "Remove the Data connection or switch this input to Connected.",
                            fieldId
                        )
                    );
                }
            }
        }
    }

    private static void ValidateEdge(
        AutomationFlowDraftEdge edge,
        AutomationFlowDraftNode source,
        AutomationFlowDraftNode target,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        if (
            !definitions.TryGetValue(source.Id, out var sourceDefinition)
            || !definitions.TryGetValue(target.Id, out var targetDefinition)
        )
        {
            return;
        }

        var output = sourceDefinition.Outputs.SingleOrDefault(port => port.Id == edge.SourcePortId);
        var input = targetDefinition.Inputs.SingleOrDefault(port => port.Id == edge.TargetPortId);
        if (output is null || input is null)
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "port-missing",
                    "Reconnect this node to an available port.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (edge.Kind == AutomationEdgeKind.Flow)
        {
            if (
                output.ValueType != AutomationPortValueType.Flow
                || input.ValueType != AutomationPortValueType.Flow
            )
            {
                errors.Add(
                    new(
                        edge.TargetNodeId,
                        "flow-port-incompatible",
                        "Connect Flow outputs only to Flow inputs.",
                        PortId: edge.TargetPortId
                    )
                );
            }
        }
        else if (
            output.ValueType == AutomationPortValueType.Flow
            || input.ValueType == AutomationPortValueType.Flow
            || output.ValueType != input.ValueType
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-type-incompatible",
                    "Connect Data ports that have the same exact type.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (sourceDefinition.Kind == AutomationNodeKind.Action)
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-source-incompatible",
                    "Use a trigger, Value, Transform, or Control output as Data.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (
            output.Nullability == AutomationPortNullability.Nullable
            && input.Nullability == AutomationPortNullability.NonNullable
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-nullability-incompatible",
                    "Connect this nullable output to an input that accepts null.",
                    PortId: edge.TargetPortId
                )
            );
        }
        else if (
            output.Sensitivity == AutomationDataSensitivity.Sensitive
            && input.Sensitivity == AutomationDataSensitivity.Safe
        )
        {
            errors.Add(
                new(
                    edge.TargetNodeId,
                    "data-sensitivity-incompatible",
                    "This input cannot accept Sensitive Data.",
                    PortId: edge.TargetPortId
                )
            );
        }
    }
}
