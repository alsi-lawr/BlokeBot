using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    internal Task<AutomationGraphValidation> ValidateAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    ) => ValidateAsync(draft, AutomationGraphAdmission.Saved, cancellationToken);

    internal Task<AutomationGraphValidation> ValidateScenarioAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    ) => ValidateAsync(draft, AutomationGraphAdmission.Scenario, cancellationToken);

    internal Task<AutomationGraphValidation> ValidateConfigurationTransferAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    ) => ValidateAsync(draft, AutomationGraphAdmission.ConfigurationTransfer, cancellationToken);

    internal Task<AutomationGraphValidation> ValidateFrozenDefinitionAsync(
        AutomationHostId hostId,
        AutomationRuntimeSerialization.PersistedFlow flow,
        CancellationToken cancellationToken
    ) =>
        flow.HostId == hostId.Value
        && RestoreFrozenDraft(flow) is AutomationFlowDraftRestoreOutcome.Available restored
            ? ValidateAsync(restored.Draft, AutomationGraphAdmission.Frozen, cancellationToken)
            : Task.FromResult<AutomationGraphValidation>(new(null, [MalformedGraphError()]));

    internal Task<AutomationGraphValidation> ValidateSubflowAsync(
        AutomationSubflowDraft draft,
        CancellationToken cancellationToken
    ) =>
        ValidateAsync(
            draft.Graph,
            AutomationGraphAdmission.Saved,
            cancellationToken,
            draft.Interface,
            draft.Id
        );

    private async Task<AutomationGraphValidation> ValidateAsync(
        AutomationFlowDraft draft,
        AutomationGraphAdmission admission,
        CancellationToken cancellationToken,
        AutomationSubflowInterface? subflowInterface = null,
        AutomationSubflowId? publishing = null
    )
    {
        if (admission != AutomationGraphAdmission.Frozen)
        {
            var snapshot = await catalog.DiscoverAsync(draft.HostId, cancellationToken);
            if (
                admission is AutomationGraphAdmission.Saved or AutomationGraphAdmission.Scenario
                && snapshot.Availability != AutomationCatalogAvailability.Enabled
            )
            {
                return new(snapshot.Availability, []);
            }
            if (
                admission == AutomationGraphAdmission.ConfigurationTransfer
                && snapshot.Availability == AutomationCatalogAvailability.HostNotFound
            )
            {
                return new(snapshot.Availability, []);
            }
        }

        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            errors.Add(new(null, "name-required", "Enter a flow name."));
        }
        else if (draft.Name.Trim().Length > 200)
        {
            errors.Add(new(null, "name-too-long", "Use 200 characters or fewer in the flow name."));
        }

        if (draft.SchemaVersion != AutomationFlowSchema.CurrentVersion)
        {
            errors.Add(new(null, "schema-invalid", "Choose a supported flow schema version."));
        }

        if (!Enum.IsDefined(draft.Canvas.Orientation) || !Enum.IsDefined(draft.Canvas.EdgeStyle))
        {
            errors.Add(new(null, "canvas-settings-invalid", "Select a supported flow layout."));
        }

        var definitions = new Dictionary<AutomationNodeId, AutomationDefinitionDescriptor>();
        var nodes = new Dictionary<AutomationNodeId, AutomationFlowDraftNode>();
        foreach (var node in draft.Nodes)
        {
            if (node.Id.Value == Guid.Empty || !nodes.TryAdd(node.Id, node))
            {
                errors.Add(new(node.Id, "node-id-invalid", "Delete this duplicate node."));
                continue;
            }

            if (node.DisplayAlias?.Length > AutomationFlowSchema.NodeDisplayAliasMaximumLength)
            {
                errors.Add(
                    new(
                        node.Id,
                        "node-alias-too-long",
                        $"Use {AutomationFlowSchema.NodeDisplayAliasMaximumLength} characters or fewer in the node name."
                    )
                );
            }

            await ValidateNodeAsync(draft.HostId, node, errors, admission, cancellationToken);
            if (
                catalog.ValidatePersistedDefinition(node.Definition)
                is AutomationConfigurationCheck.Valid valid
            )
            {
                definitions[node.Id] = valid.Definition;
            }
        }

        var sources = nodes
            .Values.Where(node =>
                definitions.TryGetValue(node.Id, out var definition)
                && definition.Kind == AutomationNodeKind.Source
            )
            .ToArray();
        var entries = nodes
            .Values.Where(node => node.Definition.TypeId == AutomationSubflowDefinitions.Entry)
            .ToArray();
        var exits = nodes
            .Values.Where(node => node.Definition.TypeId == AutomationSubflowDefinitions.Exit)
            .ToArray();
        if (subflowInterface is not null)
        {
            ValidateSubflowBoundaries(subflowInterface, sources, entries, exits, errors);
        }
        else if (entries.Length > 0 || exits.Length > 0)
        {
            errors.Add(
                new(
                    null,
                    "subflow-boundary-outside",
                    "Use entry and exit nodes only inside a subflow."
                )
            );
        }
        var roots = subflowInterface is null ? sources : entries;
        if (subflowInterface is null && sources.Length == 0)
        {
            errors.Add(new(null, "source-count", "Add one or more trigger nodes."));
        }

        var flowIncoming = nodes.Keys.ToDictionary(static id => id, static _ => 0);
        var flowAdjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var dataAdjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var combinedAdjacency = nodes.Keys.ToDictionary(
            static id => id,
            static _ => new List<AutomationNodeId>()
        );
        var dataIncoming = new Dictionary<(AutomationNodeId, AutomationPortId), int>();
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in draft.Edges)
        {
            if (edge.Id == Guid.Empty || !edgeIds.Add(edge.Id))
            {
                errors.Add(new(null, "edge-id-invalid", "Delete the duplicate connection."));
            }

            if (
                !nodes.TryGetValue(edge.SourceNodeId, out var source)
                || !nodes.TryGetValue(edge.TargetNodeId, out var target)
            )
            {
                errors.Add(new(null, "edge-node-missing", "Reconnect the saved nodes."));
                continue;
            }

            if (!Enum.IsDefined(edge.Kind))
            {
                errors.Add(
                    new(edge.TargetNodeId, "edge-kind-invalid", "Reconnect this saved connection.")
                );
                continue;
            }

            if (edge.Kind == AutomationEdgeKind.Flow)
            {
                flowIncoming[edge.TargetNodeId]++;
                flowAdjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            }
            else
            {
                var input = (edge.TargetNodeId, edge.TargetPortId);
                dataIncoming[input] = dataIncoming.GetValueOrDefault(input) + 1;
                dataAdjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            }

            combinedAdjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
            ValidateEdge(edge, source, target, definitions, errors);
        }

        foreach (var ((nodeId, _), count) in dataIncoming.Where(static pair => pair.Value > 1))
        {
            errors.Add(
                new(nodeId, "data-input-duplicate", "Keep only one Data connection for this input.")
            );
        }

        ValidateBindings(nodes, definitions, dataIncoming, errors);

        foreach (var (nodeId, count) in flowIncoming)
        {
            var kind =
                nodes.TryGetValue(nodeId, out var node)
                && definitions.TryGetValue(node.Id, out var definition)
                    ? definition.Kind
                    : (AutomationNodeKind?)null;
            if (kind == AutomationNodeKind.Source && count != 0)
            {
                errors.Add(
                    new(nodeId, "source-incoming", "Remove the input connection from this trigger.")
                );
            }
            else if (
                kind is AutomationNodeKind.Action or AutomationNodeKind.Control
                && count == 0
                && !(
                    subflowInterface is not null
                    && node!.Definition.TypeId == AutomationSubflowDefinitions.Entry
                )
            )
            {
                errors.Add(
                    new(
                        nodeId,
                        "node-disconnected",
                        "Connect this node to a trigger or another node."
                    )
                );
            }
        }

        if (roots.Length > 0)
        {
            var reached = Reachable(roots.Select(static source => source.Id), flowAdjacency);
            foreach (
                var nodeId in nodes.Keys.Where(nodeId =>
                    definitions.TryGetValue(nodeId, out var definition)
                    && definition.Kind is AutomationNodeKind.Action or AutomationNodeKind.Control
                    && !reached.Contains(nodeId)
                )
            )
            {
                errors.Add(new(nodeId, "node-disconnected", "Connect this node to a trigger."));
            }
        }

        if (subflowInterface is not null && entries.Length == 1 && exits.Length == 1)
        {
            ValidateSubflowConnectivity(
                nodes,
                definitions,
                entries[0].Id,
                exits[0].Id,
                flowAdjacency,
                dataAdjacency,
                draft.Edges,
                errors
            );
        }
        ValidateInvocationOutputAvailability(
            nodes,
            definitions,
            roots.Select(root => root.Id),
            flowAdjacency,
            dataAdjacency,
            errors
        );
        ValidateTriggerContexts(nodes, definitions, sources, flowAdjacency, errors);
        ValidateSourceAvailability(nodes, definitions, roots, draft.Edges, flowAdjacency, errors);
        ValidateSafeTriggerExpressions(draft, definitions, errors);

        if (HasCycle(flowAdjacency))
        {
            errors.Add(new(null, "flow-cycle", "Remove the Flow connection that creates a loop."));
        }

        if (HasCycle(dataAdjacency))
        {
            errors.Add(new(null, "data-cycle", "Remove the Data connection that creates a loop."));
        }

        if (!HasCycle(flowAdjacency) && !HasCycle(dataAdjacency) && HasCycle(combinedAdjacency))
        {
            errors.Add(
                new(
                    null,
                    "dependency-cycle",
                    "Remove the Flow or Data connection that creates a dependency loop."
                )
            );
        }

        if (admission is AutomationGraphAdmission.Saved or AutomationGraphAdmission.Scenario)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var enabledFeatures = await db
                .Hosts.AsNoTracking()
                .Where(value => value.Id == draft.HostId.Value)
                .Select(static value => value.EnabledFeatures)
                .SingleAsync(cancellationToken);
            await ValidateSubflowDependenciesAsync(
                db,
                draft,
                publishing,
                enabledFeatures,
                admission,
                errors,
                cancellationToken
            );
            errors.AddRange(
                CapabilityUnavailableErrors(
                    draft.Nodes.Select(static node => (node.Id, node.Definition.TypeId)),
                    enabledFeatures
                )
            );
        }

        return new(
            null,
            errors.ToImmutable(),
            subflowInterface is null
                ? []
                :
                [
                    .. definitions.Select(pair => new AutomationSubflowNodeContract(
                        pair.Key,
                        pair.Value.Kind,
                        pair.Value.Display,
                        pair.Value.Inputs,
                        pair.Value.Outputs,
                        pair.Value.Capabilities,
                        pair.Value.RetrySafety
                    )),
                ]
        );
    }
}
