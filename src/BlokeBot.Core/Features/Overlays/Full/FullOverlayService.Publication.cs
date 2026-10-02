using System.Collections.Immutable;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayService
{
    public Task<FullOverlayResult<FullOverlayPublicationSelection>> SaveAndPublishAsync(
        AuthenticatedSession session,
        SaveFullOverlayCommand command,
        CancellationToken ct
    )
    {
        if (!ValidSave(command))
        {
            return Task.FromResult(
                Reject<FullOverlayPublicationSelection>(FullOverlayRejectionKind.Invalid)
            );
        }
        var json = FullOverlayDocuments.Serialize(command.Document);
        return MutateAsync(
            session,
            new(command.OverlayId, command.ExpectedRevision),
            async (db, actor, row) =>
            {
                if (row.IsArchived)
                {
                    return Reject<FullOverlayPublicationSelection>(
                        FullOverlayRejectionKind.Archived
                    );
                }
                var admitted = await AdmitAsync(actor, row, json, ct);
                if (
                    admitted
                    is FullOverlayResult<ImmutableArray<FullOverlayDiagnostic>>.Rejected rejected
                )
                {
                    return Reject<FullOverlayPublicationSelection>(rejected.Reason);
                }
                var changedAuthority = await ReauthorizeAsync(session, actor, ct);
                if (changedAuthority is not null)
                {
                    return Reject<FullOverlayPublicationSelection>(changedAuthority);
                }
                row.Name = command.Name.Trim();
                row.DraftDocumentJson = json;
                row.PublicationSequence++;
                row.PublishedVersion = row.PublicationSequence;
                var warnings = (
                    (FullOverlayResult<ImmutableArray<FullOverlayDiagnostic>>.Succeeded)admitted
                ).Value;
                return await CommitAsync(
                    db,
                    row,
                    value => new FullOverlayPublicationSelection(ToView(value), warnings),
                    ct,
                    () =>
                    {
                        _ = db.FullOverlayPublications.Add(
                            new()
                            {
                                OverlayId = row.Id,
                                Version = row.PublicationSequence,
                                DocumentJson = json,
                                AuthorUserId = actor.UserId,
                                AuthorLogin = actor.Login,
                                PublishedAtUtc = clock.GetUtcNow().UtcDateTime,
                            }
                        );
                        return Task.CompletedTask;
                    }
                );
            },
            ct
        );
    }

    public Task<FullOverlayResult<FullOverlayPublicationSelection>> RollbackAsync(
        AuthenticatedSession session,
        RollbackFullOverlayCommand command,
        CancellationToken ct
    ) =>
        MutateAsync(
            session,
            new(command.OverlayId, command.ExpectedRevision),
            async (db, actor, row) =>
            {
                if (row.IsArchived)
                {
                    return Reject<FullOverlayPublicationSelection>(
                        FullOverlayRejectionKind.Archived
                    );
                }
                var publication = await db
                    .FullOverlayPublications.AsNoTracking()
                    .SingleOrDefaultAsync(
                        version =>
                            version.OverlayId == row.Id && version.Version == command.Version.Value,
                        ct
                    );
                if (publication is null)
                {
                    return Reject<FullOverlayPublicationSelection>(
                        FullOverlayRejectionKind.NotFound
                    );
                }
                var admitted = await AdmitAsync(actor, row, publication.DocumentJson, ct);
                if (
                    admitted
                    is FullOverlayResult<ImmutableArray<FullOverlayDiagnostic>>.Rejected rejected
                )
                {
                    return Reject<FullOverlayPublicationSelection>(rejected.Reason);
                }
                var changedAuthority = await ReauthorizeAsync(session, actor, ct);
                if (changedAuthority is not null)
                {
                    return Reject<FullOverlayPublicationSelection>(changedAuthority);
                }
                if (row.PublishedVersion != publication.Version)
                {
                    row.PublicationSequence++;
                    row.PublishedVersion = publication.Version;
                }
                var warnings = (
                    (FullOverlayResult<ImmutableArray<FullOverlayDiagnostic>>.Succeeded)admitted
                ).Value;
                return await CommitAsync(
                    db,
                    row,
                    value => new FullOverlayPublicationSelection(ToView(value), warnings),
                    ct
                );
            },
            ct
        );

    public async Task<FullOverlayResult<IReadOnlyList<FullOverlayPublicationView>>> HistoryAsync(
        AuthenticatedSession session,
        Guid overlayId,
        CancellationToken ct
    )
    {
        var authorization = await AuthorizeAsync(session, ct);
        if (authorization is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<IReadOnlyList<FullOverlayPublicationView>>(rejected.Reason);
        }
        var actor = ((FullOverlayResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db
            .FullOverlays.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.HostId == actor.HostId && row.PublicId == overlayId,
                ct
            );
        if (row is null)
        {
            return Reject<IReadOnlyList<FullOverlayPublicationView>>(
                FullOverlayRejectionKind.NotFound
            );
        }
        var history = await db
            .FullOverlayPublications.AsNoTracking()
            .Where(version => version.OverlayId == row.Id)
            .OrderByDescending(version => version.Version)
            .ToArrayAsync(ct);
        return Success<IReadOnlyList<FullOverlayPublicationView>>(
            history
                .Select(version => new FullOverlayPublicationView(
                    new(version.Version),
                    FullOverlayDocuments.Deserialize(version.DocumentJson),
                    version.AuthorUserId,
                    version.AuthorLogin,
                    Utc(version.PublishedAtUtc)
                ))
                .ToArray()
        );
    }

    public Task<FullOverlayResult<FullOverlayView>> ForgetVersionAsync(
        AuthenticatedSession session,
        ForgetFullOverlayVersionCommand command,
        CancellationToken ct
    ) =>
        MutateAsync(
            session,
            new(command.OverlayId, command.ExpectedRevision),
            async (db, actor, row) =>
                row.PublishedVersion == command.Version.Value
                    ? Reject<FullOverlayView>(FullOverlayRejectionKind.SelectedVersion)
                : !await db.FullOverlayPublications.AnyAsync(
                    version =>
                        version.OverlayId == row.Id && version.Version == command.Version.Value,
                    ct
                )
                    ? Reject<FullOverlayView>(FullOverlayRejectionKind.NotFound)
                : await CommitAsync(
                    db,
                    row,
                    ToView,
                    ct,
                    async () =>
                        _ = await db
                            .FullOverlayPublications.Where(version =>
                                version.OverlayId == row.Id
                                && version.Version == command.Version.Value
                            )
                            .ExecuteDeleteAsync(ct)
                ),
            ct
        );

    private async Task<FullOverlayResult<ImmutableArray<FullOverlayDiagnostic>>> AdmitAsync(
        OverlayManagementActor actor,
        FullOverlay row,
        string json,
        CancellationToken ct
    )
    {
        var result = await admission.AdmitAsync(
            new(actor.HostId, row.PublicId, FullOverlayDocuments.Deserialize(json)),
            ct
        );
        return result.Match<FullOverlayResult<ImmutableArray<FullOverlayDiagnostic>>>(
            admitted => Success(admitted.Warnings),
            rejected =>
                Reject<ImmutableArray<FullOverlayDiagnostic>>(
                    new FullOverlayRejection(
                        FullOverlayRejectionKind.PublicationRejected,
                        rejected.Diagnostics
                    )
                ),
            _ =>
                Reject<ImmutableArray<FullOverlayDiagnostic>>(
                    FullOverlayRejectionKind.PublicationUnavailable
                )
        );
    }
}
