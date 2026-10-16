using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal sealed record AutomationSubflowCallerDetails(
    AutomationSubflowCaller Caller,
    string Name,
    string NodeName,
    AutomationSubflowInvocationConfiguration? Invocation
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
                node.DefinitionId,
                node.DefinitionSchemaVersion,
                node.ConfigurationJson,
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
                    new(
                        caller,
                        node.Name,
                        node.DisplayAlias ?? caller.NodeId.Value.ToString(),
                        Invocation(
                            new(
                                node.DefinitionId,
                                node.DefinitionSchemaVersion,
                                System
                                    .Text.Json.JsonDocument.Parse(node.ConfigurationJson)
                                    .RootElement
                            )
                        )
                    )
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
                            authored.DisplayAlias ?? caller.NodeId.Value.ToString(),
                            Invocation(authored.Definition)
                        )
                    );
                }
            }
        }
        return result.ToImmutable();
    }

    private static AutomationSubflowInvocationConfiguration? Invocation(
        PersistedAutomationNodeDefinition definition
    ) =>
        AutomationSubflowDefinitions.TryRead(definition, out var configuration)
            ? configuration as AutomationSubflowInvocationConfiguration
            : null;

    private static async Task<
        ImmutableArray<AutomationSubflowCallerImpact>
    > IncompatibleCallersAsync(
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
        AutomationSubflowCallerImpact Impact(
            AutomationSubflowCaller caller,
            PersistedAutomationNodeDefinition definition
        ) => new(caller, Invocation(definition)?.Interface);
        return
        [
            .. ordinary
                .Select(node =>
                    Impact(
                        new(new(node.FlowId), null, new(node.Id)),
                        new(
                            node.DefinitionId,
                            node.DefinitionSchemaVersion,
                            System.Text.Json.JsonDocument.Parse(node.ConfigurationJson).RootElement
                        )
                    )
                )
                .Concat(
                    nested.Select(item =>
                        Impact(
                            new(null, new(item.CallerSubflowId), new(item.NodeId)),
                            AutomationSubflowSerialization
                                .Restore(item.SnapshotJson)
                                .Graph.Nodes.Single(node => node.Id.Value == item.NodeId)
                                .Definition
                        )
                    )
                )
                .Where(impact =>
                    impact.Interface is null
                    || !AutomationSubflowDefinitions.Compatible(impact.Interface, contract)
                ),
        ];
    }
}
