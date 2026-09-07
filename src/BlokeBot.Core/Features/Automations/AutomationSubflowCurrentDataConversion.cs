using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationSubflowCurrentDataConversion(
    IDbContextFactory<BlokeBotDbContext> factory,
    TimeProvider clock
)
{
    internal async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var after = 0;
        while (true)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var hosts = await db
                .Hosts.Where(host => host.Id > after)
                .OrderBy(host => host.Id)
                .Select(host => host.Id)
                .Take(128)
                .ToArrayAsync(cancellationToken);
            if (hosts.Length == 0)
            {
                return;
            }
            foreach (var host in hosts)
            {
                await ConvertHostAsync(host, cancellationToken);
            }
            after = hosts[^1];
        }
    }

    private async Task ConvertHostAsync(int host, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await MainDatabaseStatements.LockHostAsync(db, host, cancellationToken) == 0)
        {
            return;
        }
        var current = await (
            from subflow in db.AutomationSubflows
            join revision in db.AutomationSubflowRevisions
                on new
                {
                    subflow.HostId,
                    SubflowId = subflow.Id,
                    Revision = subflow.LastRevision,
                } equals new
                {
                    revision.HostId,
                    revision.SubflowId,
                    revision.Revision,
                }
            where subflow.HostId == host
            select new { Current = subflow, revision.SnapshotJson }
        ).ToArrayAsync(cancellationToken);
        var ordinary = await db
            .AutomationFlowNodes.Where(node =>
                node.Flow.HostId == host && node.DefinitionId == AutomationSubflowDefinitions.Invoke
            )
            .ToArrayAsync(cancellationToken);
        var changed = false;
        var converted = new List<AutomationSubflowRevision>();
        foreach (var row in current)
        {
            var revision = AutomationSubflowSerialization.Restore(row.SnapshotJson);
            var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
            var graphChanged = false;
            foreach (var node in revision.Graph.Nodes)
            {
                var definition = await ConvertAsync(node.Definition);
                graphChanged |= definition is not null;
                nodes.Add(definition is null ? node : node with { Definition = definition });
            }
            if (graphChanged)
            {
                revision = revision with
                {
                    Id = new(Guid.NewGuid()),
                    Revision = checked(row.Current.LastRevision + 1),
                    Graph = revision.Graph with { Nodes = nodes.ToImmutable() },
                    PublishedAtUtc = clock.GetUtcNow(),
                };
                row.Current.LastRevision = revision.Revision;
                _ = db.AutomationSubflowRevisions.Add(
                    new()
                    {
                        HostId = host,
                        Id = revision.Id.Value,
                        SubflowId = revision.SubflowId.Value,
                        Revision = revision.Revision,
                        SnapshotJson = AutomationSubflowSerialization.Serialize(revision),
                    }
                );
                changed = true;
            }
            converted.Add(revision);
        }
        foreach (var node in ordinary)
        {
            using var json = JsonDocument.Parse(node.ConfigurationJson);
            var definition = await ConvertAsync(
                new(node.DefinitionId, node.DefinitionSchemaVersion, json.RootElement)
            );
            if (definition is null)
            {
                continue;
            }
            node.DefinitionSchemaVersion = definition.SchemaVersion;
            node.ConfigurationJson = definition.Configuration.GetRawText();
            changed = true;
        }
        if (!changed)
        {
            return;
        }
        _ = await db
            .AutomationSubflowCallers.Where(row => row.HostId == host)
            .ExecuteDeleteAsync(cancellationToken);
        _ = await db
            .AutomationSubflowNestedCallers.Where(row => row.HostId == host)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var node in ordinary)
        {
            using var json = JsonDocument.Parse(node.ConfigurationJson);
            if (
                AutomationSubflowDefinitions.TryRead(
                    new(node.DefinitionId, node.DefinitionSchemaVersion, json.RootElement),
                    out var configuration
                ) && configuration is AutomationSubflowInvocationConfiguration call
            )
            {
                _ = db.AutomationSubflowCallers.Add(
                    new()
                    {
                        HostId = host,
                        NodeId = node.Id,
                        SubflowId = call.SubflowId.Value,
                    }
                );
            }
        }
        foreach (var revision in converted)
        {
            foreach (
                var call in AutomationSubflowStore
                    .Calls(revision.Graph.Nodes)
                    .Where(call => call.SubflowId.Value != Guid.Empty)
            )
            {
                _ = db.AutomationSubflowNestedCallers.Add(
                    new()
                    {
                        HostId = host,
                        CallerSubflowId = revision.SubflowId.Value,
                        NodeId = call.NodeId.Value,
                        SubflowId = call.SubflowId.Value,
                    }
                );
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        async Task<PersistedAutomationNodeDefinition?> ConvertAsync(
            PersistedAutomationNodeDefinition definition
        )
        {
            if (
                AutomationSubflowDefinitions.CheckFrozen(definition)
                is not AutomationConfigurationCheck.Valid
                {
                    Configuration: AutomationFrozenSubflowConfiguration old
                }
            )
            {
                return null;
            }
            var stable = await db
                .AutomationSubflowRevisions.AsNoTracking()
                .Where(row => row.HostId == host && row.Id == old.RevisionId.Value)
                .Select(row => (Guid?)row.SubflowId)
                .SingleOrDefaultAsync(cancellationToken);
            return stable is null
                ? null
                : new(
                    AutomationSubflowDefinitions.Invoke,
                    2,
                    JsonSerializer.SerializeToElement(
                        new
                        {
                            SubflowId = new AutomationSubflowId(stable.Value),
                            old.Interface,
                            FixedInputs = AutomationDataValueSerialization.SerializeOutputs(
                                old.FixedInputs
                            ),
                        },
                        AutomationSubflowDefinitions.JsonOptions
                    )
                );
        }
    }
}
