using BlokeBot.Core.Auth.Sessions;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayService
{
    public Task<FullOverlayResult<FullOverlayView>> ArchiveAsync(
        AuthenticatedSession session,
        FullOverlayMutation command,
        CancellationToken ct
    ) =>
        MutateAsync(
            session,
            command,
            async (db, _, row) =>
            {
                row.IsArchived = true;
                return await CommitAsync(db, row, ToView, ct);
            },
            ct
        );

    public Task<FullOverlayResult<FullOverlayView>> RestoreAsync(
        AuthenticatedSession session,
        FullOverlayMutation command,
        CancellationToken ct
    ) =>
        MutateAsync(
            session,
            command,
            async (db, _, row) =>
            {
                row.IsArchived = false;
                return await CommitAsync(db, row, ToView, ct);
            },
            ct
        );

    public Task<FullOverlayResult<FullOverlayCreation>> DuplicateAsync(
        AuthenticatedSession session,
        DuplicateFullOverlayCommand command,
        CancellationToken ct
    ) =>
        !ValidName(command.Name)
            ? Task.FromResult(Reject<FullOverlayCreation>(FullOverlayRejectionKind.Invalid))
            : MutateAsync(
                session,
                new(command.OverlayId, command.ExpectedRevision),
                async (db, actor, row) =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    if (!await LockEnabledHostAsync(db, actor.HostId, ct))
                    {
                        return Reject<FullOverlayCreation>(
                            FullOverlayRejectionKind.FeatureDisabled
                        );
                    }
                    if (
                        !await db.FullOverlays.AnyAsync(
                            value => value.Id == row.Id && value.Revision == row.Revision,
                            ct
                        )
                    )
                    {
                        return Reject<FullOverlayCreation>(FullOverlayRejectionKind.Conflict);
                    }
                    var created = NewOverlay(
                        actor.HostId,
                        command.Name,
                        FullOverlayDocuments.Deserialize(row.DraftDocumentJson)
                    );
                    _ = db.FullOverlays.Add(created.Row);
                    _ = await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    await NotifyAsync();
                    return Success(new FullOverlayCreation(ToView(created.Row), created.Access));
                },
                ct
            );

    public Task<FullOverlayResult<Guid>> DeleteAsync(
        AuthenticatedSession session,
        DeleteFullOverlayCommand command,
        CancellationToken ct
    ) =>
        command.Confirmation != FullOverlayDeleteConfirmation.Confirmed
            ? Task.FromResult(Reject<Guid>(FullOverlayRejectionKind.ConfirmationRequired))
            : MutateAsync(
                session,
                new(command.OverlayId, command.ExpectedRevision),
                async (db, actor, row) =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    if (!await LockEnabledHostAsync(db, actor.HostId, ct))
                    {
                        return Reject<Guid>(FullOverlayRejectionKind.FeatureDisabled);
                    }
                    var deleted = await db
                        .FullOverlays.Where(value =>
                            value.Id == row.Id && value.Revision == row.Revision
                        )
                        .ExecuteDeleteAsync(ct);
                    if (deleted != 1)
                    {
                        return Reject<Guid>(FullOverlayRejectionKind.Conflict);
                    }
                    await transaction.CommitAsync(ct);
                    await NotifyAsync();
                    return Success(row.PublicId);
                },
                ct
            );
}
