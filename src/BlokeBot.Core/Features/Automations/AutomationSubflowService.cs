using System.Collections.Immutable;
using System.Text;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationSubflowService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationFlowService flows,
    TimeProvider clock
)
{
    public async Task<ImmutableArray<AutomationSubflowRevision>> ListAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db
            .AutomationSubflowRevisions.AsNoTracking()
            .Where(row => row.HostId == hostId.Value)
            .OrderBy(row => row.SubflowId)
            .ThenBy(row => row.Revision)
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => AutomationSubflowSerialization.Restore(row.SnapshotJson))];
    }

    public async Task<AutomationSubflowClosureOutcome> LoadClosureAsync(
        AutomationHostId hostId,
        ImmutableArray<AutomationSubflowRevisionId> roots,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await AutomationSubflowStore.LoadClosureAsync(
            db,
            hostId,
            roots,
            null,
            cancellationToken
        );
    }

    public async Task<AutomationSubflowPublishOutcome> PublishAsync(
        AutomationSubflowDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (
            draft.Id.Value == Guid.Empty
            || draft.Description is null
            || draft.Description.Length > 2000
            || draft.Graph.Id is not null
            || draft.Graph.IsEnabled
            || !AutomationSubflowDefinitions.ValidInterface(draft.Interface)
            || draft.Graph.Nodes.IsDefault
            || draft.Graph.Nodes.Length is < 2 or > 256
            || draft.Graph.Edges.IsDefault
            || draft.Graph.Edges.Length > 1024
        )
        {
            return Invalid(
                "subflow-contract-invalid",
                "Use bounded metadata, typed ports and a source-free subflow graph."
            );
        }
        var validation = await flows.ValidateSubflowAsync(draft, cancellationToken);
        if (validation.Gate is not null)
        {
            return Invalid("subflow-host-unavailable", "Enable automations for this host.");
        }
        if (!validation.Errors.IsEmpty)
        {
            return new AutomationSubflowPublishOutcome.Invalid(validation.Errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (
            await MainDatabaseStatements.LockHostAsync(
                db,
                draft.Graph.HostId.Value,
                cancellationToken
            ) == 0
        )
        {
            return Invalid("subflow-host-unavailable", "Enable automations for this host.");
        }
        var pinErrors = await AutomationSubflowStore.ValidatePinsAsync(
            db,
            draft.Graph,
            cancellationToken
        );
        if (!pinErrors.IsEmpty)
        {
            return new AutomationSubflowPublishOutcome.Invalid(pinErrors);
        }
        var loaded = await AutomationSubflowStore.LoadClosureAsync(
            db,
            draft.Graph.HostId,
            AutomationSubflowStore.Pins(draft.Graph.Nodes).Select(pin => pin.RevisionId),
            draft.Id,
            cancellationToken
        );
        if (loaded is AutomationSubflowClosureOutcome.Invalid invalid)
        {
            return new AutomationSubflowPublishOutcome.Invalid(invalid.Errors);
        }
        var closure = ((AutomationSubflowClosureOutcome.Available)loaded).Closure;
        var hostId = draft.Graph.HostId.Value;
        var previous = await db
            .AutomationSubflowRevisions.AsNoTracking()
            .Where(row => row.HostId == hostId && row.SubflowId == draft.Id.Value)
            .OrderByDescending(row => row.Revision)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous is not null)
        {
            var rebindErrors = AutomationSubflowStore.RebindErrors(
                AutomationSubflowSerialization.Restore(previous.SnapshotJson).Graph,
                draft.Graph
            );
            if (!rebindErrors.IsEmpty)
            {
                return new AutomationSubflowPublishOutcome.Invalid(rebindErrors);
            }
        }
        var updated = await db
            .AutomationSubflows.Where(row => row.HostId == hostId && row.Id == draft.Id.Value)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.LastRevision, row => row.LastRevision + 1),
                cancellationToken
            );
        int number;
        if (updated == 0)
        {
            number = 1;
            _ = db.AutomationSubflows.Add(
                new()
                {
                    HostId = hostId,
                    Id = draft.Id.Value,
                    LastRevision = number,
                }
            );
        }
        else
        {
            number = await db
                .AutomationSubflows.Where(row => row.HostId == hostId && row.Id == draft.Id.Value)
                .Select(row => row.LastRevision)
                .SingleAsync(cancellationToken);
        }
        var allNodes = draft
            .Graph.Nodes.Concat(closure.Revisions.SelectMany(revision => revision.Graph.Nodes))
            .ToArray();
        var revision = new AutomationSubflowRevision(
            new(Guid.NewGuid()),
            draft.Id,
            number,
            draft.Description.Trim(),
            draft.Interface,
            draft.Graph with
            {
                Name = draft.Graph.Name.Trim(),
            },
            validation.NodeContracts,
            AutomationRequiredFeatures.ForDefinitions(
                allNodes.Select(node => node.Definition.TypeId)
            ),
            [
                .. allNodes
                    .Select(node => node.Definition.PluginProvenance)
                    .OfType<AutomationPluginProvenance>()
                    .Distinct(),
            ],
            clock.GetUtcNow()
        );
        var enabled = await db
            .Hosts.Where(host => host.Id == hostId)
            .Select(host => host.EnabledFeatures)
            .SingleAsync(cancellationToken);
        if ((revision.RequiredFeatures & ~enabled) != HostFeatureFlags.None)
        {
            return Invalid(
                "capability-unavailable",
                "Enable the subflow's required host features."
            );
        }
        var json = AutomationSubflowSerialization.Serialize(revision);
        if (Encoding.UTF8.GetByteCount(json) > 524288)
        {
            return Invalid("subflow-size", "Reduce the subflow snapshot size.");
        }
        var incompatible = await IncompatibleCallersAsync(
            db,
            hostId,
            draft.Id,
            draft.Interface,
            cancellationToken
        );
        _ = db.AutomationSubflowRevisions.Add(
            new()
            {
                HostId = hostId,
                Id = revision.Id.Value,
                SubflowId = draft.Id.Value,
                Revision = number,
                SnapshotJson = json,
            }
        );
        db.AutomationSubflowRevisionReferences.AddRange(
            AutomationSubflowStore
                .Pins(draft.Graph.Nodes)
                .Select(pin => new AutomationSubflowRevisionReference
                {
                    HostId = hostId,
                    CallerRevisionId = revision.Id.Value,
                    NodeId = pin.NodeId.Value,
                    RevisionId = pin.RevisionId.Value,
                })
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AutomationSubflowPublishOutcome.Published(revision, incompatible);
    }

    public async Task<AutomationSubflowRemovalOutcome> RemoveRevisionAsync(
        AutomationHostId hostId,
        AutomationSubflowRevisionId revisionId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await MainDatabaseStatements.LockHostAsync(db, hostId.Value, cancellationToken) == 0)
        {
            return AutomationSubflowRemovalOutcome.NotFound;
        }
        var rows = db.AutomationSubflowRevisions.Where(row =>
            row.HostId == hostId.Value && row.Id == revisionId.Value
        );
        var removed = await rows.Where(row =>
                !db.AutomationSubflowCallers.Any(reference =>
                    reference.HostId == row.HostId && reference.RevisionId == row.Id
                )
                && !db.AutomationSubflowRevisionReferences.Any(reference =>
                    reference.HostId == row.HostId && reference.RevisionId == row.Id
                )
                && !db.AutomationSubflowRunReferences.Any(reference =>
                    reference.HostId == row.HostId && reference.RevisionId == row.Id
                )
            )
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return removed != 0 ? AutomationSubflowRemovalOutcome.Removed
            : await rows.AnyAsync(cancellationToken) ? AutomationSubflowRemovalOutcome.Referenced
            : AutomationSubflowRemovalOutcome.NotFound;
    }

    private static AutomationSubflowPublishOutcome.Invalid Invalid(string code, string message) =>
        new([AutomationSubflowStore.Error(code, message)]);

    private static async Task<ImmutableArray<AutomationSubflowCaller>> IncompatibleCallersAsync(
        BlokeBotDbContext db,
        int hostId,
        AutomationSubflowId subflowId,
        AutomationSubflowInterface contract,
        CancellationToken cancellationToken
    )
    {
        var previous = await db
            .AutomationSubflowRevisions.AsNoTracking()
            .Where(row => row.HostId == hostId && row.SubflowId == subflowId.Value)
            .ToArrayAsync(cancellationToken);
        var incompatibleIds = previous
            .Where(row =>
                !AutomationSubflowDefinitions.Compatible(
                    AutomationSubflowSerialization.Restore(row.SnapshotJson).Interface,
                    contract
                )
            )
            .Select(row => row.Id)
            .ToArray();
        var ordinary = await (
            from reference in db.AutomationSubflowCallers.AsNoTracking()
            join node in db.AutomationFlowNodes.AsNoTracking() on reference.NodeId equals node.Id
            where reference.HostId == hostId && incompatibleIds.Contains(reference.RevisionId)
            select new { node.FlowId, reference.NodeId }
        ).ToArrayAsync(cancellationToken);
        var nested = await db
            .AutomationSubflowRevisionReferences.AsNoTracking()
            .Where(reference =>
                reference.HostId == hostId && incompatibleIds.Contains(reference.RevisionId)
            )
            .ToArrayAsync(cancellationToken);
        return
        [
            .. ordinary.Select(value => new AutomationSubflowCaller(
                new(value.FlowId),
                null,
                new(value.NodeId)
            )),
            .. nested.Select(value => new AutomationSubflowCaller(
                null,
                new(value.CallerRevisionId),
                new(value.NodeId)
            )),
        ];
    }
}
