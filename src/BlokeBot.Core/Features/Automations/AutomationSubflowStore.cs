using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationSubflowStore
{
    internal const int MaximumDepth = 8;
    internal const int MaximumClosureRevisions = 128;

    internal static async Task<AutomationSubflowClosureOutcome> LoadClosureAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        IEnumerable<AutomationSubflowId> roots,
        AutomationSubflowId? publishing,
        CancellationToken cancellationToken,
        AutomationSubflowRevision? preparedCandidate = null
    )
    {
        var revisions = new Dictionary<AutomationSubflowId, AutomationSubflowRevision>();
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        var visits = 0;
        foreach (var root in roots.Distinct())
        {
            await Visit(root, publishing is { } owner ? [owner] : [], 1);
            if (errors.Count > 0)
            {
                break;
            }
        }
        var closure = new AutomationSubflowClosure([.. revisions.Values]);
        foreach (var revision in closure.Revisions)
        {
            errors.AddRange(ValidateCalls(revision.Graph, closure));
        }
        return errors.Count > 0
            ? new AutomationSubflowClosureOutcome.Invalid(errors.ToImmutable())
            : new AutomationSubflowClosureOutcome.Available(closure);

        async Task Visit(AutomationSubflowId id, HashSet<AutomationSubflowId> ancestors, int depth)
        {
            if (++visits > 1024)
            {
                errors.Add(
                    Error("subflow-closure-limit", "Use fewer repeated nested subflow calls.")
                );
                return;
            }
            if (depth + (publishing is null ? 0 : 1) > MaximumDepth)
            {
                errors.Add(Error("subflow-depth", "Reduce the nesting depth of this subflow."));
                return;
            }
            if (!revisions.TryGetValue(id, out var revision))
            {
                if (revisions.Count >= MaximumClosureRevisions)
                {
                    errors.Add(Error("subflow-closure-limit", "Use fewer subflows."));
                    return;
                }
                if (
                    preparedCandidate is { } candidate
                    && candidate.SubflowId == id
                    && candidate.Graph.HostId == hostId
                )
                {
                    revision = candidate;
                }
                else
                {
                    var json = await (
                        from current in db.AutomationSubflows.AsNoTracking()
                        join stored in db.AutomationSubflowRevisions.AsNoTracking()
                            on new
                            {
                                current.HostId,
                                SubflowId = current.Id,
                                Revision = current.LastRevision,
                            } equals new
                            {
                                stored.HostId,
                                stored.SubflowId,
                                stored.Revision,
                            }
                        where current.HostId == hostId.Value && current.Id == id.Value
                        select stored.SnapshotJson
                    ).SingleOrDefaultAsync(cancellationToken);
                    if (json is null)
                    {
                        errors.Add(Error("subflow-missing", "Choose an available subflow."));
                        return;
                    }
                    revision = AutomationSubflowSerialization.Restore(json);
                }
                revisions.Add(id, revision);
            }
            if (!ancestors.Add(id))
            {
                errors.Add(Error("subflow-recursion", "Remove the recursive subflow call."));
                return;
            }
            foreach (var child in Calls(revision.Graph.Nodes))
            {
                await Visit(child.SubflowId, ancestors, depth + 1);
                if (errors.Count > 0)
                {
                    break;
                }
            }
            _ = ancestors.Remove(id);
        }
    }

    internal static IEnumerable<(AutomationNodeId NodeId, AutomationSubflowId SubflowId)> Calls(
        IEnumerable<AutomationFlowDraftNode> nodes
    ) =>
        nodes
            .Where(node => node.Definition.TypeId == AutomationSubflowDefinitions.Invoke)
            .Select(node =>
                (
                    node.Id,
                    AutomationSubflowDefinitions.TryRead(node.Definition, out var configuration)
                    && configuration is AutomationSubflowInvocationConfiguration call
                        ? call.SubflowId
                        : default
                )
            );

    internal static ImmutableArray<AutomationGraphError> ValidateCalls(
        AutomationFlowDraft graph,
        AutomationSubflowClosure closure
    )
    {
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        var revisions = closure.Revisions.ToDictionary(revision => revision.SubflowId);
        foreach (
            var node in graph.Nodes.Where(node =>
                node.Definition.TypeId == AutomationSubflowDefinitions.Invoke
            )
        )
        {
            if (
                !AutomationSubflowDefinitions.TryRead(node.Definition, out var configuration)
                || configuration is not AutomationSubflowInvocationConfiguration call
                || !revisions.TryGetValue(call.SubflowId, out var revision)
                || revision.Graph.HostId != graph.HostId
            )
            {
                errors.Add(Error("subflow-missing", "Choose an available subflow.", node.Id));
            }
            else if (!AutomationSubflowDefinitions.Compatible(call.Interface, revision.Interface))
            {
                errors.Add(
                    Error(
                        "subflow-interface-incompatible",
                        "Update this call's interface and inputs.",
                        node.Id
                    )
                );
            }
        }
        return errors.ToImmutable();
    }

    internal static AutomationGraphError Error(
        string code,
        string message,
        AutomationNodeId? nodeId = null
    ) => new(nodeId, code, message);
}
