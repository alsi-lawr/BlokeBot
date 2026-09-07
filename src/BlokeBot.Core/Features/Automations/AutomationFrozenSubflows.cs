using System.Text;
using BlokeBot.Persistence;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationFrozenInvocation(
    Guid CallerId,
    Guid EntryId,
    Guid ExitId,
    string Path
);

internal sealed record AutomationFrozenGraph(
    AutomationRuntimeSerialization.PersistedFlow Flow,
    AutomationSubflowClosure Closure
);

internal static class AutomationFrozenSubflows
{
    internal const int MaximumNodes = 4096;
    internal const int MaximumEdges = 16384;
    internal const int MaximumInvocations = 1024;
    internal const int MaximumSnapshotBytes = 8 * 1024 * 1024;

    internal static async Task<AutomationFrozenGraph?> FreezeAsync(
        BlokeBotDbContext db,
        AutomationRuntimeSerialization.PersistedFlow root,
        Guid traceId,
        CancellationToken cancellationToken
    )
    {
        var pins = root
            .Nodes.Where(node => node.DefinitionId == AutomationSubflowDefinitions.Invoke)
            .Select(node =>
                AutomationSubflowDefinitions.TryRead(
                    AutomationRuntimeSerialization.Definition(node),
                    out var configuration
                )
                    ? configuration.RevisionId!.Value
                    : default
            )
            .ToArray();
        var result = await AutomationSubflowStore.LoadClosureAsync(
            db,
            new(root.HostId),
            pins,
            null,
            cancellationToken
        );
        if (result is not AutomationSubflowClosureOutcome.Available available)
        {
            return null;
        }
        if (pins.Length == 0)
        {
            return new(root, available.Closure);
        }
        var revisions = available.Closure.Revisions.ToDictionary(revision => revision.Id);
        var bytes = Encoding.UTF8.GetByteCount(
            AutomationRuntimeSerialization.SerializeDefinition(root)
        );
        var nodes = root.Nodes.ToList();
        var edges = root.Edges.ToList();
        var invocations = new List<AutomationFrozenInvocation>();
        var used = root
            .Nodes.Select(node => node.Id)
            .Concat(root.Edges.Select(edge => edge.Id))
            .Append(traceId)
            .Append(Guid.Empty)
            .ToHashSet();
        var ordinal = 0;
        Guid Allocate()
        {
            Guid id;
            do
            {
                id = new Guid(++ordinal, 0, 0, new byte[8]);
            } while (!used.Add(id));
            return id;
        }
        bool Expand(
            AutomationRuntimeSerialization.PersistedNode caller,
            string parentPath,
            int depth
        )
        {
            if (
                depth > AutomationSubflowStore.MaximumDepth
                || invocations.Count >= MaximumInvocations
                || !AutomationSubflowDefinitions.TryRead(
                    AutomationRuntimeSerialization.Definition(caller),
                    out var configuration
                )
                || !revisions.TryGetValue(configuration.RevisionId!.Value, out var revision)
                || !AutomationSubflowDefinitions.SameInterface(
                    configuration.Interface,
                    revision.Interface
                )
                || nodes.Count + revision.Graph.Nodes.Length > MaximumNodes
                || edges.Count + revision.Graph.Edges.Length + 1 > MaximumEdges
                || (
                    bytes += Encoding.UTF8.GetByteCount(
                        AutomationSubflowSerialization.Serialize(revision)
                    )
                ) > MaximumSnapshotBytes
            )
            {
                return false;
            }
            var graph = AutomationScenarioGraph.FreezeGraph(revision.Graph);
            var ids = graph
                .Nodes.OrderBy(node => node.Id)
                .ToDictionary(node => node.Id, _ => Allocate());
            var entry = ids[
                graph
                    .Nodes.Single(node => node.DefinitionId == AutomationSubflowDefinitions.Entry)
                    .Id
            ];
            var exit = ids[
                graph
                    .Nodes.Single(node => node.DefinitionId == AutomationSubflowDefinitions.Exit)
                    .Id
            ];
            var path =
                parentPath
                + "/"
                + (caller.AuthorNodeId ?? caller.Id).ToString("N")
                + ":"
                + revision.Id.Value.ToString("N");
            var trace = new AutomationTraceInvocation(
                entry,
                caller.Invocation?.Id,
                revision.SubflowId.Value,
                revision.Id.Value
            );
            invocations.Add(new(caller.Id, entry, exit, path));
            var expanded = graph
                .Nodes.Select(node =>
                    node with
                    {
                        Id = ids[node.Id],
                        AuthorNodeId = node.Id,
                        Invocation = trace,
                        Contract = revision.NodeContracts.Single(contract =>
                            contract.NodeId.Value == node.Id
                        ),
                        PluginProvenanceJson = revision
                            .Graph.Nodes.Single(authored => authored.Id.Value == node.Id)
                            .Definition.PluginProvenance
                            is { } provenance
                            ? PluginAutomationCatalogRegistry.SerializeProvenance(provenance)
                            : null,
                    }
                )
                .ToArray();
            nodes.AddRange(expanded);
            for (var index = 0; index < edges.Count; index++)
            {
                if (
                    edges[index] is { Kind: AutomationEdgeKind.Flow } edge
                    && edge.SourceNodeId == caller.Id
                )
                {
                    edges[index] = edge with { SourceNodeId = exit };
                }
            }
            edges.Add(
                new(Allocate(), AutomationEdgeKind.Flow, caller.Id, "complete", entry, "flow")
            );
            edges.AddRange(
                graph
                    .Edges.OrderBy(edge => edge.Id)
                    .Select(edge =>
                        edge with
                        {
                            Id = Allocate(),
                            SourceNodeId = ids[edge.SourceNodeId],
                            TargetNodeId = ids[edge.TargetNodeId],
                        }
                    )
            );
            return expanded
                .Where(node => node.DefinitionId == AutomationSubflowDefinitions.Invoke)
                .OrderBy(node => node.AuthorNodeId)
                .All(node => Expand(node, path, depth + 1));
        }
        if (
            !root
                .Nodes.Where(node => node.DefinitionId == AutomationSubflowDefinitions.Invoke)
                .OrderBy(node => node.Id)
                .All(node => Expand(node, "", 1))
        )
        {
            return null;
        }
        var frozen = root with
        {
            Nodes = [.. nodes],
            Edges = [.. edges],
            Invocations = [.. invocations],
        };
        return WithinSnapshotBound(frozen) ? new(frozen, available.Closure) : null;
    }

    internal static bool WithinSnapshotBound(AutomationRuntimeSerialization.PersistedFlow flow) =>
        Encoding.UTF8.GetByteCount(AutomationRuntimeSerialization.SerializeDefinition(flow))
        <= MaximumSnapshotBytes;

    internal static bool HasActiveDescendants(
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationFrozenInvocation invocation,
        IReadOnlySet<Guid> activeNodes
    ) =>
        flow.Nodes.Any(node =>
            node.Id != invocation.ExitId
            && activeNodes.Contains(node.Id)
            && node.Invocation is { } nested
            && flow.Invocations.Single(candidate => candidate.EntryId == nested.Id)
                .Path.StartsWith(invocation.Path, StringComparison.Ordinal)
        );

    internal static AutomationConfigurationCheck WithContract(
        AutomationRuntimeSerialization.PersistedNode node,
        AutomationConfigurationCheck check
    ) =>
        check is AutomationConfigurationCheck.Valid valid && node.Contract is { } contract
            ? valid with
            {
                Definition = valid.Definition with
                {
                    Kind = contract.Kind,
                    Display = contract.Display,
                    Inputs = contract.Inputs,
                    Outputs = contract.Outputs,
                    Capabilities = contract.Capabilities,
                    RetrySafety = contract.RetrySafety,
                },
            }
            : check;

    internal static IEnumerable<AutomationFrozenInvocation> Unwind(
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode node
    )
    {
        // Exit has no authored continuation: failure cannot publish an invocation's typed outputs.
        while (
            (!node.ContinueOnFailure || node.DefinitionId == AutomationSubflowDefinitions.Exit)
            && node.Invocation is { } parent
        )
        {
            var invocation = flow.Invocations.Single(value => value.EntryId == parent.Id);
            yield return invocation;
            node = flow.Nodes.Single(value => value.Id == invocation.CallerId);
        }
    }

    internal static AutomationTraceInvocation Trace(
        AutomationTraceInvocation invocation,
        Guid root
    ) => invocation with { ParentId = invocation.ParentId ?? root };

    internal static AutomationFrozenInvocation? Invocation(
        AutomationRuntimeSerialization.PersistedFlow flow,
        Guid caller
    ) =>
        flow.Invocations.IsDefault
            ? null
            : flow.Invocations.FirstOrDefault(invocation => invocation.CallerId == caller);

    internal static AutomationRuntimeSerialization.PersistedNode Continuation(
        AutomationRuntimeSerialization.PersistedFlow flow,
        AutomationRuntimeSerialization.PersistedNode node
    ) =>
        Invocation(flow, node.Id) is { } invocation
            ? flow.Nodes.Single(candidate => candidate.Id == invocation.ExitId)
            : node;
}
