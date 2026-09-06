using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationScenarioGraph
{
    internal static AutomationRuntimeSerialization.PersistedFlow FreezeGraph(
        AutomationFlowDraft draft
    ) =>
        new(
            draft.Id?.Value ?? Guid.Empty,
            draft.HostId.Value,
            draft.SchemaVersion,
            draft
                .Nodes.Select(static node => new AutomationRuntimeSerialization.PersistedNode(
                    node.Id.Value,
                    node.Definition.TypeId,
                    node.Definition.SchemaVersion,
                    node.Definition.Configuration.GetRawText(),
                    AutomationRuntimeSerialization.SerializeInputBindings(node.InputBindings),
                    node.ExpressionLanguageVersion.Value,
                    node.FailurePolicy == AutomationNodeFailurePolicy.Continue
                ))
                .ToImmutableArray(),
            draft
                .Edges.Select(static edge => new AutomationRuntimeSerialization.PersistedEdge(
                    edge.Id,
                    edge.Kind,
                    edge.SourceNodeId.Value,
                    edge.SourcePortId.Value,
                    edge.TargetNodeId.Value,
                    edge.TargetPortId.Value
                ))
                .ToImmutableArray()
        );
}
