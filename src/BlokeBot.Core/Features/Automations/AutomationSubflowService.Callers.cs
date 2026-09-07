using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationSubflowCallerDetails(
    AutomationSubflowCaller Caller,
    string Name,
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
        var subflowIds = page.Where(caller => caller.SubflowId is not null)
            .Select(caller => caller.SubflowId!.Value.Value)
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
        var snapshots = await (
            from current in db.AutomationSubflows.AsNoTracking()
            join revision in db.AutomationSubflowRevisions.AsNoTracking()
                on new
                {
                    current.HostId,
                    SubflowId = current.Id,
                    Revision = current.LastRevision,
                } equals new
                {
                    revision.HostId,
                    revision.SubflowId,
                    revision.Revision,
                }
            where current.HostId == host.Value && subflowIds.Contains(current.Id)
            select revision.SnapshotJson
        ).ToArrayAsync(cancellationToken);
        var revisions = snapshots
            .Select(AutomationSubflowSerialization.Restore)
            .ToDictionary(revision => revision.SubflowId);
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
                    new(caller, node.Name, node.DisplayAlias ?? caller.NodeId.Value.ToString())
                );
            }
            else if (caller.SubflowId is { } id && revisions.TryGetValue(id, out var revision))
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
                            authored.DisplayAlias ?? caller.NodeId.Value.ToString()
                        )
                    );
                }
            }
        }
        return result.ToImmutable();
    }

    private static async Task<ImmutableArray<AutomationSubflowCaller>> IncompatibleCallersAsync(
        BlokeBotDbContext db,
        int hostId,
        AutomationSubflowId subflowId,
        AutomationSubflowInterface contract,
        CancellationToken cancellationToken
    )
    {
        var ordinary = await (
            from reference in db.AutomationSubflowCallers.AsNoTracking()
            join node in db.AutomationFlowNodes.AsNoTracking() on reference.NodeId equals node.Id
            where reference.HostId == hostId && reference.SubflowId == subflowId.Value
            select node
        ).ToArrayAsync(cancellationToken);
        var nested = await (
            from reference in db.AutomationSubflowNestedCallers.AsNoTracking()
            join current in db.AutomationSubflows.AsNoTracking()
                on new { reference.HostId, Id = reference.CallerSubflowId } equals new
                {
                    current.HostId,
                    current.Id,
                }
            join revision in db.AutomationSubflowRevisions.AsNoTracking()
                on new
                {
                    current.HostId,
                    SubflowId = current.Id,
                    Revision = current.LastRevision,
                } equals new
                {
                    revision.HostId,
                    revision.SubflowId,
                    revision.Revision,
                }
            where reference.HostId == hostId && reference.SubflowId == subflowId.Value
            select new
            {
                reference.NodeId,
                reference.CallerSubflowId,
                revision.SnapshotJson,
            }
        ).ToArrayAsync(cancellationToken);
        bool Incompatible(PersistedAutomationNodeDefinition definition) =>
            !AutomationSubflowDefinitions.TryRead(definition, out var call)
            || !AutomationSubflowDefinitions.Compatible(call.Interface, contract);
        return
        [
            .. ordinary
                .Where(node =>
                    Incompatible(
                        new(
                            node.DefinitionId,
                            node.DefinitionSchemaVersion,
                            System.Text.Json.JsonDocument.Parse(node.ConfigurationJson).RootElement
                        )
                    )
                )
                .Select(node => new AutomationSubflowCaller(new(node.FlowId), null, new(node.Id))),
            .. nested
                .Where(item =>
                    Incompatible(
                        AutomationSubflowSerialization
                            .Restore(item.SnapshotJson)
                            .Graph.Nodes.Single(node => node.Id.Value == item.NodeId)
                            .Definition
                    )
                )
                .Select(item => new AutomationSubflowCaller(
                    null,
                    new(item.CallerSubflowId),
                    new(item.NodeId)
                )),
        ];
    }
}
