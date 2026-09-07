using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    private async Task ValidateSubflowDependenciesAsync(
        BlokeBotDbContext db,
        AutomationFlowDraft draft,
        AutomationSubflowId? publishing,
        HostFeatureFlags enabledFeatures,
        AutomationGraphAdmission admission,
        ImmutableArray<AutomationGraphError>.Builder errors,
        CancellationToken cancellationToken,
        AutomationSubflowRevision? preparedCandidate
    )
    {
        if (draft.Nodes.Any(node => node.Definition.TypeId == AutomationSubflowDefinitions.Invoke))
        {
            errors.AddRange(
                await AutomationSubflowStore.ValidatePinsAsync(
                    db,
                    draft,
                    cancellationToken,
                    preparedCandidate
                )
            );
            var closure = await AutomationSubflowStore.LoadClosureAsync(
                db,
                draft.HostId,
                AutomationSubflowStore.Pins(draft.Nodes).Select(pin => pin.RevisionId),
                publishing,
                cancellationToken,
                preparedCandidate
            );
            if (closure is AutomationSubflowClosureOutcome.Invalid invalid)
            {
                errors.AddRange(invalid.Errors);
            }
            else if (closure is AutomationSubflowClosureOutcome.Available available)
            {
                foreach (var revision in available.Closure.Revisions)
                {
                    errors.AddRange(
                        CapabilityUnavailableErrors(
                            revision.Graph.Nodes.Select(node => (node.Id, node.Definition.TypeId)),
                            enabledFeatures
                        )
                    );
                    foreach (var node in revision.Graph.Nodes)
                    {
                        await ValidateNodeAsync(
                            draft.HostId,
                            node,
                            errors,
                            admission,
                            cancellationToken
                        );
                    }
                }
            }
        }
    }

    private static void ValidateSubflowBoundaries(
        AutomationSubflowInterface contract,
        AutomationFlowDraftNode[] sources,
        AutomationFlowDraftNode[] entries,
        AutomationFlowDraftNode[] exits,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        if (sources.Length != 0 || entries.Length != 1 || exits.Length != 1)
        {
            errors.Add(
                new(
                    null,
                    "subflow-boundaries",
                    "Use exactly one entry and exit, without a trigger."
                )
            );
        }
        foreach (var boundary in entries.Concat(exits))
        {
            if (
                !AutomationSubflowDefinitions.TryRead(boundary.Definition, out var configuration)
                || !AutomationSubflowDefinitions.SameInterface(contract, configuration.Interface)
            )
            {
                errors.Add(
                    new(
                        boundary.Id,
                        "subflow-boundary-interface",
                        "Use the published subflow interface on both boundaries."
                    )
                );
            }
        }
    }

    private static void ValidateSubflowConnectivity(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        AutomationNodeId entry,
        AutomationNodeId exit,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> flow,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> data,
        ImmutableArray<AutomationFlowDraftEdge> edges,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        var reverse = nodes.Keys.ToDictionary(id => id, _ => new List<AutomationNodeId>());
        foreach (var (source, targets) in flow)
        {
            foreach (var target in targets)
            {
                reverse[target].Add(source);
            }
        }
        var reachesExit = Reachable([exit], reverse);
        foreach (var node in nodes.Values)
        {
            if (
                definitions.TryGetValue(node.Id, out var contract)
                && contract.Outputs.Any(port =>
                    port.ValueType == AutomationPortValueType.Flow
                    && !edges.Any(edge =>
                        edge.Kind == AutomationEdgeKind.Flow
                        && edge.SourceNodeId == node.Id
                        && edge.SourcePortId == port.Id
                    )
                )
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "subflow-flow-output-disconnected",
                        "Connect every Flow output to the subflow exit."
                    )
                );
            }

            if (
                definitions.TryGetValue(node.Id, out var definition)
                && definition.Kind is AutomationNodeKind.Control or AutomationNodeKind.Action
                && !reachesExit.Contains(node.Id)
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "subflow-exit-unreachable",
                        "Connect every Flow node to the subflow exit."
                    )
                );
            }
        }
        var connected = Reachable([entry], flow);
        foreach (var (source, targets) in data)
        {
            foreach (var target in targets)
            {
                reverse[target].Add(source);
            }
        }
        connected.UnionWith(Reachable(connected.ToArray(), reverse));
        foreach (var node in nodes.Keys.Where(id => !connected.Contains(id)))
        {
            errors.Add(new(node, "subflow-node-unused", "Connect this node to the subflow graph."));
        }
    }

    private static void ValidateInvocationOutputAvailability(
        IReadOnlyDictionary<AutomationNodeId, AutomationFlowDraftNode> nodes,
        IReadOnlyDictionary<AutomationNodeId, AutomationDefinitionDescriptor> definitions,
        IEnumerable<AutomationNodeId> roots,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> flow,
        IReadOnlyDictionary<AutomationNodeId, List<AutomationNodeId>> data,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (
            var invocation in nodes.Values.Where(node =>
                node.Definition.TypeId == AutomationSubflowDefinitions.Invoke
            )
        )
        {
            var withoutInvocation = flow.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == invocation.Id ? new List<AutomationNodeId>() : pair.Value
            );
            var bypassing = Reachable(roots.Where(id => id != invocation.Id), withoutInvocation);
            var afterInvocation = Reachable([invocation.Id], flow);
            foreach (
                var consumer in Reachable([invocation.Id], data).Where(id => id != invocation.Id)
            )
            {
                if (
                    definitions.TryGetValue(consumer, out var definition)
                    && definition.Kind is AutomationNodeKind.Action or AutomationNodeKind.Control
                    && (!afterInvocation.Contains(consumer) || bypassing.Contains(consumer))
                )
                {
                    errors.Add(
                        new(
                            consumer,
                            "subflow-output-unavailable",
                            "Run this invocation on every Flow path before consuming its outputs."
                        )
                    );
                }
            }
        }
    }

    private static void ValidateSubflowFixedInputs(
        AutomationFlowDraftNode node,
        AutomationSubflowConfiguration configuration,
        AutomationDefinitionDescriptor descriptor,
        ImmutableArray<AutomationGraphError>.Builder errors
    )
    {
        foreach (var (portId, value) in configuration.FixedInputs)
        {
            var port = descriptor.Inputs.FirstOrDefault(input => input.Id == portId);
            if (
                port is null
                || port.ValueType == AutomationPortValueType.Flow
                || AutomationPureHandlerRegistry.ValueType(value.Value) != port.ValueType
                || (
                    value.Value is AutomationValue.Null
                    && port.Nullability == AutomationPortNullability.NonNullable
                )
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "subflow-fixed-type",
                        "Use a fixed value matching the declared input type.",
                        PortId: portId
                    )
                );
            }
        }
        foreach (
            var input in descriptor.Inputs.Where(input =>
                input.ValueType != AutomationPortValueType.Flow
            )
        )
        {
            if (
                node.InputBindings.GetValueOrDefault(input.BindingFieldId!.Value)?.Mode
                    == AutomationInputBindingMode.Fixed
                && !configuration.FixedInputs.ContainsKey(input.Id)
            )
            {
                errors.Add(
                    new(
                        node.Id,
                        "subflow-fixed-missing",
                        "Provide a typed fixed value for this input.",
                        PortId: input.Id
                    )
                );
            }
        }
    }
}
