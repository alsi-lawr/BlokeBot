using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationSubflowService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationFlowService flows,
    TimeProvider clock
)
{
    public const int LibraryPageSize = 20;

    public async Task<AutomationSubflowLibraryPage> ListAsync(
        AutomationHostId hostId,
        AutomationSubflowLibraryQuery query,
        CancellationToken cancellationToken
    )
    {
        var search = query.Search.Trim().ToLowerInvariant();
        if (search.Length > 200)
        {
            return new([], null);
        }
        var offset = Math.Max(query.Offset, 0);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await MainDatabaseStatements
            .QuerySubflowLibrary(db, hostId.Value)
            .Where(row => row.Name.ToLower().Contains(search))
            .OrderBy(row => row.SubflowId)
            .ThenByDescending(row => row.Revision)
            .Skip(offset)
            .Take(LibraryPageSize + 1)
            .ToArrayAsync(cancellationToken);
        return new(
            [
                .. rows.Take(LibraryPageSize)
                    .Select(row => new AutomationSubflowLibrarySummary(
                        new(row.Id),
                        new(row.SubflowId),
                        row.Revision,
                        row.Name,
                        row.Description
                    )),
            ],
            rows.Length > LibraryPageSize ? checked(offset + LibraryPageSize) : null
        );
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

    public async Task<AutomationSubflowPreviewOutcome> PreviewAsync(
        AutomationSubflowDraft draft,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateDraftAsync(draft, cancellationToken);
        if (!validation.Errors.IsEmpty)
        {
            return new AutomationSubflowPreviewOutcome.Invalid(validation.Errors);
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await PrepareAsync(db, draft, validation, cancellationToken) switch
        {
            Preparation.Ready ready => new AutomationSubflowPreviewOutcome.Ready(
                ready.Callers,
                ready.Revision
            ),
            Preparation.Invalid invalid => new AutomationSubflowPreviewOutcome.Invalid(
                invalid.Errors
            ),
            _ => throw new InvalidOperationException(),
        };
    }

    public async Task<AutomationSubflowPublishOutcome> PublishAsync(
        AutomationSubflowDraft draft,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateDraftAsync(draft, cancellationToken);
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
        var prepared = await PrepareAsync(db, draft, validation, cancellationToken);
        if (prepared is Preparation.Invalid invalid)
        {
            return new AutomationSubflowPublishOutcome.Invalid(invalid.Errors);
        }
        var ready = (Preparation.Ready)prepared;
        var revision = ready.Revision;
        var hostId = draft.Graph.HostId.Value;
        var updated = await db
            .AutomationSubflows.Where(row => row.HostId == hostId && row.Id == draft.Id.Value)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.LastRevision, revision.Revision),
                cancellationToken
            );
        if (updated == 0)
        {
            _ = db.AutomationSubflows.Add(
                new()
                {
                    HostId = hostId,
                    Id = draft.Id.Value,
                    LastRevision = revision.Revision,
                }
            );
        }
        _ = db.AutomationSubflowRevisions.Add(
            new()
            {
                HostId = hostId,
                Id = revision.Id.Value,
                SubflowId = draft.Id.Value,
                Revision = revision.Revision,
                SnapshotJson = ready.Json,
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
        return new AutomationSubflowPublishOutcome.Published(revision, ready.Callers);
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
