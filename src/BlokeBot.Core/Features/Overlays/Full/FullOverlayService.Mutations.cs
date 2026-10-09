using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayService
{
    private async Task<FullOverlayResult<OverlayManagementActor>> AuthorizeAsync(
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        var result = await authority.AuthorizeAsync(session, ct);
        return Authorization(result);
    }

    private static FullOverlayResult<OverlayManagementActor> Authorization(
        OverlayManagementAuthorization result
    ) =>
        result switch
        {
            OverlayManagementAuthorization.Granted granted => Success(granted.Actor),
            OverlayManagementAuthorization.Rejected
            {
                Reason: OverlayManagementRejection.ParentDisabled
            } => Reject<OverlayManagementActor>(FullOverlayRejectionKind.FeatureDisabled),
            OverlayManagementAuthorization.Rejected
            {
                Reason: OverlayManagementRejection.Missing
            } => Reject<OverlayManagementActor>(FullOverlayRejectionKind.NotFound),
            _ => Reject<OverlayManagementActor>(FullOverlayRejectionKind.Unauthorized),
        };

    private async Task<FullOverlayResult<T>> MutateAsync<T>(
        AuthenticatedSession session,
        FullOverlayMutation command,
        Func<
            BlokeBotDbContext,
            OverlayManagementActor,
            FullOverlay,
            Task<FullOverlayResult<T>>
        > mutate,
        CancellationToken ct
    )
    {
        if (command.OverlayId == Guid.Empty || command.ExpectedRevision.Value <= 0)
        {
            return Reject<T>(FullOverlayRejectionKind.Invalid);
        }
        var authorization = await AuthorizeAsync(session, ct);
        if (authorization is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<T>(rejected.Reason);
        }
        var actor = ((FullOverlayResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db
            .FullOverlays.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.HostId == actor.HostId && row.PublicId == command.OverlayId,
                ct
            );
        return row switch
        {
            null => Reject<T>(FullOverlayRejectionKind.NotFound),
            { Revision: var revision } when revision != command.ExpectedRevision.Value => Reject<T>(
                FullOverlayRejectionKind.Conflict
            ),
            { } current => await mutate(db, actor, current),
        };
    }

    private async Task<FullOverlayResult<T>> CommitAsync<T>(
        BlokeBotDbContext db,
        FullOverlay row,
        Func<FullOverlay, T> project,
        CancellationToken ct,
        Func<Task>? persist = null
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await LockEnabledHostAsync(db, row.HostId, ct))
        {
            return Reject<T>(FullOverlayRejectionKind.FeatureDisabled);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db
            .FullOverlays.Where(current => current.Id == row.Id && current.Revision == row.Revision)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(current => current.Name, row.Name)
                        .SetProperty(current => current.DraftDocumentJson, row.DraftDocumentJson)
                        .SetProperty(current => current.IsArchived, row.IsArchived)
                        .SetProperty(
                            current => current.PublicationSequence,
                            row.PublicationSequence
                        )
                        .SetProperty(current => current.PublishedVersion, row.PublishedVersion)
                        .SetProperty(current => current.Revision, current => current.Revision + 1)
                        .SetProperty(current => current.UpdatedAtUtc, now),
                ct
            );
        if (updated != 1)
        {
            return Reject<T>(FullOverlayRejectionKind.Conflict);
        }
        if (persist is not null)
        {
            await persist();
        }
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        row.Revision++;
        row.UpdatedAtUtc = now;
        await NotifyAsync();
        return Success(project(row));
    }

    private static async Task<bool> LockEnabledHostAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await MainDatabaseStatements.LockHostAsync(db, hostId, ct) == 1
        && await db.Hosts.AnyAsync(
            host =>
                host.Id == hostId
                && (host.EnabledFeatures & HostFeatureFlags.Overlays) == HostFeatureFlags.Overlays,
            ct
        );

    private async Task<FullOverlayRejection?> ReauthorizeAsync(
        AuthenticatedSession session,
        OverlayManagementActor actor,
        CancellationToken ct
    )
    {
        var current = await AuthorizeAsync(session, ct);
        return current.Match<FullOverlayRejection?>(
            granted =>
                granted.Value == actor ? null : new(FullOverlayRejectionKind.Unauthorized, []),
            rejected => rejected.Reason
        );
    }
}
