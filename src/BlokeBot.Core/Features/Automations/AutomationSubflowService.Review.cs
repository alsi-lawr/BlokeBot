using System.Text.Json;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationSubflowService
{
    private static bool SameReview(AutomationSubflowReview left, AutomationSubflowReview right) =>
        left.Draft.Id == right.Draft.Id
        && AutomationFlowService.SameEffects(left.Effects, right.Effects)
        && AutomationFlowService.SameEffectParents(left.EffectParents, right.EffectParents)
        && left.PreviousRevision == right.PreviousRevision
        && SameInterface(left.PreviousInterface, right.PreviousInterface)
        && SameInterface(left.Draft.Interface, right.Draft.Interface)
        && SameGraph(left.Draft.Graph, right.Draft.Graph)
        && left.Dependencies.Count == right.Dependencies.Count
        && left.Dependencies.All(pair =>
            right.Dependencies.TryGetValue(pair.Key, out var id) && id.Id == pair.Value.Id
        )
        && left.RequiredFeatures == right.RequiredFeatures
        && left.Plugins.Length == right.Plugins.Length
        && left.Plugins.All(right.Plugins.Contains)
        && left.NodeContracts.Length == right.NodeContracts.Length
        && left.NodeContracts.All(contract =>
            right.NodeContracts.Any(current =>
                current.NodeId == contract.NodeId
                && current.Kind == contract.Kind
                && current.Capabilities == contract.Capabilities
                && current.RetrySafety == contract.RetrySafety
                && SameInterface(
                    new(contract.Inputs, contract.Outputs),
                    new(current.Inputs, current.Outputs)
                )
            )
        )
        && left.Callers.Length == right.Callers.Length
        && left.Callers.All(impact =>
            right.Callers.Any(current =>
                current.Caller == impact.Caller
                && SameInterface(current.Interface, impact.Interface)
            )
        );

    private static bool SameInterface(
        AutomationSubflowInterface? left,
        AutomationSubflowInterface? right
    ) =>
        left is null
            ? right is null
            : right is not null
                && left.Inputs.OrderBy(port => port.Id.Value, StringComparer.Ordinal)
                    .SequenceEqual(
                        right.Inputs.OrderBy(port => port.Id.Value, StringComparer.Ordinal)
                    )
                && left.Outputs.OrderBy(port => port.Id.Value, StringComparer.Ordinal)
                    .SequenceEqual(
                        right.Outputs.OrderBy(port => port.Id.Value, StringComparer.Ordinal)
                    );

    internal static bool SameGraph(AutomationFlowDraft left, AutomationFlowDraft right) =>
        left.HostId == right.HostId
        && left.SchemaVersion == right.SchemaVersion
        && left.Nodes.Length == right.Nodes.Length
        && left.Edges.OrderBy(edge => edge.Id).SequenceEqual(right.Edges.OrderBy(edge => edge.Id))
        && left.Nodes.All(node =>
            right.Nodes.Any(current =>
                current.Id == node.Id
                && current.ExpressionLanguageVersion == node.ExpressionLanguageVersion
                && current.FailurePolicy == node.FailurePolicy
                && current.Definition.TypeId == node.Definition.TypeId
                && current.Definition.SchemaVersion == node.Definition.SchemaVersion
                && current.Definition.PluginProvenance == node.Definition.PluginProvenance
                && JsonElement.DeepEquals(
                    current.Definition.Configuration,
                    node.Definition.Configuration
                )
                && current.InputBindings.Count == node.InputBindings.Count
                && current.InputBindings.All(pair =>
                    node.InputBindings.TryGetValue(pair.Key, out var binding)
                    && binding == pair.Value
                )
            )
        );
}
