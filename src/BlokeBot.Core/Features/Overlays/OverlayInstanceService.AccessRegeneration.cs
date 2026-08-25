using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

public sealed partial class OverlayInstanceService
{
    public async Task<OverlayInstanceResult<OverlayInstanceKeyRotation>> RotateKeyAsync(
        AuthenticatedSession session,
        RotateOverlayInstanceKeyCommand command,
        CancellationToken ct
    ) =>
        !ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision)
            ? Rejected<OverlayInstanceKeyRotation>(
                new OverlayInstanceRejection.Invalid(
                    "An overlay ID and positive expected revision are required."
                )
            )
            : await MutateExistingAsync<OverlayInstanceKeyRotation>(
                session,
                command.OverlayId,
                command.ExpectedRevision,
                "key-rotated",
                async (db, actor, overlay) =>
                {
                    await using var alertOperation = overlay.RequiresAccessKeyRegeneration
                        ? await alerts.BeginReportOperationAsync(ct)
                        : null;
                    var accessKey = GenerateAccessKey();
                    var digest = OverlayAccessKeyDigest.Compute(accessKey);
                    var now = Now();
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    var updated = await db
                        .OverlayInstances.Where(value =>
                            value.HostId == actor.HostId
                            && value.PublicId == command.OverlayId
                            && value.Revision == command.ExpectedRevision.Value
                        )
                        .ExecuteUpdateAsync(
                            setters =>
                                setters
                                    .SetProperty(value => value.AccessKeyDigest, digest)
                                    .SetProperty(
                                        value => value.RequiresAccessKeyRegeneration,
                                        false
                                    )
                                    .SetProperty(
                                        value => value.KeyVersion,
                                        value => value.KeyVersion + 1
                                    )
                                    .SetProperty(value => value.UpdatedAtUtc, now)
                                    .SetProperty(
                                        value => value.Revision,
                                        value => value.Revision + 1
                                    ),
                            ct
                        );
                    if (updated != 1)
                    {
                        return Rejected<OverlayInstanceKeyRotation>(
                            new OverlayInstanceRejection.Conflict()
                        );
                    }

                    overlay.AccessKeyDigest = digest;
                    overlay.RequiresAccessKeyRegeneration = false;
                    overlay.KeyVersion++;
                    overlay.UpdatedAtUtc = now;
                    overlay.Revision++;
                    _ = db.OverlayInstanceEvents.Add(
                        DomainEvent(actor, overlay, OverlayInstanceEventKind.KeyRotated, now)
                    );
                    var alertResolution = await ResolveRegenerationAlertIfCompleteAsync(
                        db,
                        actor,
                        alertOperation,
                        now,
                        ct
                    );
                    _ = await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    if (alertOperation is not null && alertResolution is not null)
                    {
                        await alertOperation.PublishCommittedAsync(alertResolution);
                    }
                    await NotifyAsync(
                        actor,
                        overlay.PublicId,
                        OverlayInstanceEventKind.KeyRotated,
                        overlay.Revision,
                        ct
                    );
                    return Succeeded(
                        new OverlayInstanceKeyRotation(
                            ToView(overlay),
                            new OverlayPrivateAccess(accessKey)
                        )
                    );
                },
                ct
            );

    public async Task<OverlayInstanceResult<Guid>> DeleteAsync(
        AuthenticatedSession session,
        DeleteOverlayInstanceCommand command,
        CancellationToken ct
    ) =>
        !ValidOverlayIdAndRevision(command.OverlayId, command.ExpectedRevision)
            ? Rejected<Guid>(
                new OverlayInstanceRejection.Invalid(
                    "An overlay ID and positive expected revision are required."
                )
            )
            : await MutateExistingAsync<Guid>(
                session,
                command.OverlayId,
                command.ExpectedRevision,
                "deleted",
                async (db, actor, overlay) =>
                {
                    await using var alertOperation = overlay.RequiresAccessKeyRegeneration
                        ? await alerts.BeginReportOperationAsync(ct)
                        : null;
                    var now = Now();
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    var deleted = await db
                        .OverlayInstances.Where(value =>
                            value.HostId == actor.HostId
                            && value.PublicId == command.OverlayId
                            && value.Revision == command.ExpectedRevision.Value
                        )
                        .ExecuteDeleteAsync(ct);
                    if (deleted != 1)
                    {
                        return Rejected<Guid>(new OverlayInstanceRejection.Conflict());
                    }

                    overlay.Revision++;
                    _ = db.OverlayInstanceEvents.Add(
                        DomainEvent(actor, overlay, OverlayInstanceEventKind.Deleted, now)
                    );
                    var alertResolution = await ResolveRegenerationAlertIfCompleteAsync(
                        db,
                        actor,
                        alertOperation,
                        now,
                        ct
                    );
                    _ = await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    if (alertOperation is not null && alertResolution is not null)
                    {
                        await alertOperation.PublishCommittedAsync(alertResolution);
                    }
                    await NotifyAsync(
                        actor,
                        overlay.PublicId,
                        OverlayInstanceEventKind.Deleted,
                        overlay.Revision,
                        ct
                    );
                    return Succeeded(command.OverlayId);
                },
                ct
            );

    private static async Task<DurableAlertPendingResolution?> ResolveRegenerationAlertIfCompleteAsync(
        BlokeBotDbContext db,
        AuthorizedActor actor,
        DurableAlertService.ReportOperation? alertOperation,
        DateTime resolvedAtUtc,
        CancellationToken cancellationToken
    ) =>
        alertOperation is null
        || await db
            .OverlayInstances.AsNoTracking()
            .AnyAsync(
                value => value.HostId == actor.HostId && value.RequiresAccessKeyRegeneration,
                cancellationToken
            )
            ? null
            : await alertOperation.StageResolutionAsync(
                db,
                new(
                    actor.HostId,
                    OverlayAccessRegeneration.AlertSource,
                    OverlayAccessRegeneration.AlertSourceKey
                ),
                actor.Login,
                resolvedAtUtc,
                cancellationToken
            );
}
