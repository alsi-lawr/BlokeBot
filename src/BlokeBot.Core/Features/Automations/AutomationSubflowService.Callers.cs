using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationSubflowCallerDetails(
    AutomationSubflowCaller Caller,
    string Name,
    int? Revision,
    string NodeName
);

public sealed partial class AutomationSubflowService
{
    internal async Task<ImmutableArray<AutomationSubflowCallerDetails>> DescribeCallersAsync(
        AutomationHostId host,
        IEnumerable<AutomationSubflowCaller> callers,
        CancellationToken cancellationToken
    )
    {
        var page = callers.Take(LibraryPageSize).ToArray();
        var nodeIds = page.Where(caller => caller.FlowId is not null)
            .Select(caller => caller.NodeId.Value)
            .ToArray();
        var revisionIds = page.Where(caller => caller.RevisionId is not null)
            .Select(caller => caller.RevisionId!.Value.Value)
            .Distinct()
            .ToArray();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var ordinary = await db
            .AutomationFlowNodes.AsNoTracking()
            .Where(node => node.Flow.HostId == host.Value && nodeIds.Contains(node.Id))
            .Select(node => new
            {
                node.Id,
                node.FlowId,
                node.Flow.Name,
                node.DisplayAlias,
            })
            .ToArrayAsync(cancellationToken);
        var snapshots = await db
            .AutomationSubflowRevisions.AsNoTracking()
            .Where(revision => revision.HostId == host.Value && revisionIds.Contains(revision.Id))
            .Select(revision => revision.SnapshotJson)
            .ToArrayAsync(cancellationToken);
        var revisions = snapshots
            .Select(AutomationSubflowSerialization.Restore)
            .ToDictionary(revision => revision.Id);
        var result = ImmutableArray.CreateBuilder<AutomationSubflowCallerDetails>();
        foreach (var caller in page)
        {
            if (
                caller.FlowId is { } flow
                && ordinary.FirstOrDefault(node =>
                    node.Id == caller.NodeId.Value && node.FlowId == flow.Value
                )
                    is { } node
            )
            {
                result.Add(
                    new(
                        caller,
                        node.Name,
                        null,
                        node.DisplayAlias ?? caller.NodeId.Value.ToString()
                    )
                );
            }
            else if (caller.RevisionId is { } id && revisions.TryGetValue(id, out var revision))
            {
                var authored = revision.Graph.Nodes.FirstOrDefault(node =>
                    node.Id == caller.NodeId
                );
                if (authored is not null)
                {
                    result.Add(
                        new(
                            caller,
                            revision.Graph.Name,
                            revision.Revision,
                            authored.DisplayAlias ?? caller.NodeId.Value.ToString()
                        )
                    );
                }
            }
        }
        return result.ToImmutable();
    }
}
