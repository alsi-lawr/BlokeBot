using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationSubflowStore
{
    internal const int MaximumDepth = 8;
    internal const int MaximumClosureRevisions = 128;

    internal static async Task<AutomationSubflowClosureOutcome> LoadClosureAsync(
        BlokeBotDbContext db,
        AutomationHostId hostId,
        IEnumerable<AutomationSubflowRevisionId> roots,
        AutomationSubflowId? publishing,
        CancellationToken cancellationToken
    )
    {
        var revisions = new Dictionary<AutomationSubflowRevisionId, AutomationSubflowRevision>();
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
        return errors.Count > 0
            ? new AutomationSubflowClosureOutcome.Invalid(errors.ToImmutable())
            : new AutomationSubflowClosureOutcome.Available(new([.. revisions.Values]));

        async Task Visit(
            AutomationSubflowRevisionId id,
            HashSet<AutomationSubflowId> ancestors,
            int depth
        )
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
                    errors.Add(Error("subflow-closure-limit", "Use fewer subflow revisions."));
                    return;
                }
                var row = await db
                    .AutomationSubflowRevisions.AsNoTracking()
                    .SingleOrDefaultAsync(
                        value => value.HostId == hostId.Value && value.Id == id.Value,
                        cancellationToken
                    );
                if (row is null)
                {
                    errors.Add(
                        Error(
                            "subflow-revision-missing",
                            "Select a subflow revision owned by this host."
                        )
                    );
                    return;
                }
                revision = AutomationSubflowSerialization.Restore(row.SnapshotJson);
                revisions.Add(id, revision);
            }
            if (!ancestors.Add(revision.SubflowId))
            {
                errors.Add(Error("subflow-recursion", "Remove the recursive subflow call."));
                return;
            }
            foreach (var child in Pins(revision.Graph.Nodes))
            {
                await Visit(child.RevisionId, ancestors, depth + 1);
                if (errors.Count > 0)
                {
                    break;
                }
            }
            _ = ancestors.Remove(revision.SubflowId);
        }
    }

    internal static IEnumerable<(
        AutomationNodeId NodeId,
        AutomationSubflowRevisionId RevisionId
    )> Pins(IEnumerable<AutomationFlowDraftNode> nodes) =>
        nodes
            .Where(node => node.Definition.TypeId == AutomationSubflowDefinitions.Invoke)
            .Select(node =>
                (
                    node.Id,
                    AutomationSubflowDefinitions.TryRead(node.Definition, out var configuration)
                        ? configuration.RevisionId!.Value
                        : default
                )
            );

    internal static async Task<ImmutableArray<AutomationGraphError>> ValidatePinsAsync(
        BlokeBotDbContext db,
        AutomationFlowDraft graph,
        CancellationToken cancellationToken
    )
    {
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        foreach (
            var node in graph.Nodes.Where(node =>
                node.Definition.TypeId == AutomationSubflowDefinitions.Invoke
            )
        )
        {
            if (!AutomationSubflowDefinitions.TryRead(node.Definition, out var configuration))
            {
                errors.Add(
                    Error("subflow-pin-invalid", "Select a valid subflow revision.", node.Id)
                );
                continue;
            }
            var row = await db
                .AutomationSubflowRevisions.AsNoTracking()
                .SingleOrDefaultAsync(
                    value =>
                        value.HostId == graph.HostId.Value
                        && value.Id == configuration.RevisionId!.Value.Value,
                    cancellationToken
                );
            if (row is not null)
            {
                var enabled = await db
                    .Hosts.Where(host => host.Id == graph.HostId.Value)
                    .Select(host => host.EnabledFeatures)
                    .SingleAsync(cancellationToken);
                if (
                    (
                        AutomationSubflowSerialization.Restore(row.SnapshotJson).RequiredFeatures
                        & ~enabled
                    ) != HostFeatureFlags.None
                )
                {
                    errors.Add(
                        Error(
                            "capability-unavailable",
                            "Enable the selected subflow's required host features.",
                            node.Id
                        )
                    );
                }
            }
            if (
                row is null
                || !AutomationSubflowDefinitions.SameInterface(
                    configuration.Interface,
                    AutomationSubflowSerialization.Restore(row.SnapshotJson).Interface
                )
            )
            {
                errors.Add(
                    Error(
                        "subflow-pin-invalid",
                        "Use the stored interface of a revision owned by this host.",
                        node.Id
                    )
                );
            }
        }
        return errors.ToImmutable();
    }

    internal static ImmutableArray<AutomationGraphError> RebindErrors(
        AutomationFlowDraft existing,
        AutomationFlowDraft candidate
    )
    {
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        foreach (var node in candidate.Nodes)
        {
            var before = existing.Nodes.FirstOrDefault(value => value.Id == node.Id);
            if (
                before is null
                || !AutomationSubflowDefinitions.TryRead(before.Definition, out var oldPin)
                || before.Definition.TypeId != AutomationSubflowDefinitions.Invoke
                || !AutomationSubflowDefinitions.TryRead(node.Definition, out var newPin)
                || node.Definition.TypeId != AutomationSubflowDefinitions.Invoke
            )
            {
                continue;
            }
            if (
                oldPin.RevisionId != newPin.RevisionId
                && !AutomationSubflowDefinitions.Compatible(oldPin.Interface, newPin.Interface)
            )
            {
                errors.Add(
                    Error(
                        "subflow-rebind-incompatible",
                        "Replace this invocation to use an incompatible interface.",
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
