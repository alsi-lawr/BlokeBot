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
                        new(row.SubflowId),
                        row.Name,
                        row.Description
                    )),
            ],
            rows.Length > LibraryPageSize ? checked(offset + LibraryPageSize) : null
        );
    }

    public async Task<AutomationSubflowRevision?> LoadCurrentAsync(
        AutomationHostId host,
        AutomationSubflowId id,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var json = await (
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
            where current.HostId == host.Value && current.Id == id.Value
            select revision.SnapshotJson
        ).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : AutomationSubflowSerialization.Restore(json);
    }

    public async Task<AutomationSubflowClosureOutcome> LoadClosureAsync(
        AutomationHostId hostId,
        ImmutableArray<AutomationSubflowId> roots,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return await MainDatabaseStatements.LockHostAsync(db, hostId.Value, cancellationToken) == 0
            ? new AutomationSubflowClosureOutcome.Invalid([
                AutomationSubflowStore.Error(
                    "subflow-host-unavailable",
                    "Choose an available channel."
                ),
            ])
            : await AutomationSubflowStore.LoadClosureAsync(
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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return
            await MainDatabaseStatements.LockHostAsync(
                db,
                draft.Graph.HostId.Value,
                cancellationToken
            ) == 0
            ? new AutomationSubflowPreviewOutcome.Invalid([
                AutomationSubflowStore.Error(
                    "subflow-host-unavailable",
                    "Enable automations for this host."
                ),
            ])
            : await PrepareAsync(db, draft, validation, cancellationToken) switch
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
        _ = await db
            .AutomationSubflowNestedCallers.Where(row =>
                row.HostId == hostId && row.CallerSubflowId == draft.Id.Value
            )
            .ExecuteDeleteAsync(cancellationToken);
        db.AutomationSubflowNestedCallers.AddRange(
            AutomationSubflowStore
                .Calls(draft.Graph.Nodes)
                .Select(pin => new AutomationSubflowNestedCallerReference
                {
                    HostId = hostId,
                    CallerSubflowId = draft.Id.Value,
                    NodeId = pin.NodeId.Value,
                    SubflowId = pin.SubflowId.Value,
                })
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AutomationSubflowPublishOutcome.Published(revision, ready.Callers);
    }

    internal async Task<AutomationSubflowRemovalOutcome> RemoveRevisionAsync(
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
        var target = await rows.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var removed = await rows.Where(row =>
                (
                    !db.AutomationSubflows.Any(current =>
                        current.HostId == row.HostId
                        && current.Id == row.SubflowId
                        && current.LastRevision == row.Revision
                    )
                    || (
                        !db.AutomationSubflowCallers.Any(reference =>
                            reference.HostId == row.HostId && reference.SubflowId == row.SubflowId
                        )
                        && !db.AutomationSubflowNestedCallers.Any(reference =>
                            reference.HostId == row.HostId && reference.SubflowId == row.SubflowId
                        )
                    )
                )
                && !db.AutomationSubflowRunReferences.Any(reference =>
                    reference.HostId == row.HostId && reference.RevisionId == row.Id
                )
            )
            .ExecuteDeleteAsync(cancellationToken);
        if (
            removed != 0
            && target is not null
            && await db.AutomationSubflows.AnyAsync(
                current =>
                    current.HostId == target.HostId
                    && current.Id == target.SubflowId
                    && current.LastRevision == target.Revision,
                cancellationToken
            )
        )
        {
            _ = await db
                .AutomationSubflowNestedCallers.Where(reference =>
                    reference.HostId == target.HostId
                    && reference.CallerSubflowId == target.SubflowId
                )
                .ExecuteDeleteAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return removed != 0 ? AutomationSubflowRemovalOutcome.Removed
            : await rows.AnyAsync(cancellationToken) ? AutomationSubflowRemovalOutcome.Referenced
            : AutomationSubflowRemovalOutcome.NotFound;
    }

    private static AutomationSubflowPublishOutcome.Invalid Invalid(string code, string message) =>
        new([AutomationSubflowStore.Error(code, message)]);
}
