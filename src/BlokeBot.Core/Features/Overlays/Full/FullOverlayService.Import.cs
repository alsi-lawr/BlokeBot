using System.Data;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer.FullOverlays;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayService
{
    internal async Task<FullOverlayResult<FullOverlayImportApplied>> ApplyImportAsync(
        AuthenticatedSession session,
        OverlayManagementActor expectedActor,
        FullOverlayPackage package,
        OverlayCueService.OverlayMediaTransfer media,
        FullOverlayPortability portability,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!ValidName(package.Name) || !FullOverlayDocuments.HasCoherentIdentity(package.Document))
        {
            return Reject<FullOverlayImportApplied>(FullOverlayRejectionKind.Invalid);
        }
        if (await ReauthorizeAsync(session, expectedActor, ct) is { } denied)
        {
            return Reject<FullOverlayImportApplied>(denied);
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct
        );
        if (!await LockEnabledHostAsync(db, expectedActor.HostId, ct))
        {
            return Reject<FullOverlayImportApplied>(FullOverlayRejectionKind.FeatureDisabled);
        }
        var declarations = portability.Capture();
        var attached = await media.AttachAsync(db, expectedActor.HostId, package.Assets, ct);
        if (attached is OverlayCueResult<IReadOnlyDictionary<Guid, Guid>>.Rejected invalid)
        {
            return Reject<FullOverlayImportApplied>(
                new FullOverlayRejection(
                    FullOverlayRejectionKind.Invalid,
                    [
                        new(
                            "media-import-rejected",
                            invalid.Reason.Message,
                            FullOverlayDiagnosticSeverity.Error
                        ),
                    ]
                )
            );
        }
        var mapped = portability.Map(
            package.Document,
            declarations,
            ((OverlayCueResult<IReadOnlyDictionary<Guid, Guid>>.Succeeded)attached).Value
        );
        var created = NewOverlay(expectedActor.HostId, package.Name, mapped);
        _ = db.FullOverlays.Add(created.Row);
        _ = await db.SaveChangesAsync(ct);
        var rechecked = Authorization(await authority.AuthorizeAsync(session, db, ct));
        if (rechecked is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<FullOverlayImportApplied>(rejected.Reason);
        }
        if (((FullOverlayResult<OverlayManagementActor>.Succeeded)rechecked).Value != expectedActor)
        {
            return Reject<FullOverlayImportApplied>(FullOverlayRejectionKind.Unauthorized);
        }
        if (!portability.IsCurrent(declarations))
        {
            return Reject<FullOverlayImportApplied>(FullOverlayRejectionKind.Conflict);
        }
        ct.ThrowIfCancellationRequested();
        // Once commit starts, caller cancellation cannot turn a committed import into a retry.
        await transaction.CommitAsync(CancellationToken.None);
        media.Committed();
        var notificationPending = false;
        try
        {
            notificationPending =
                await events.PublishAsync(AppEventKind.OverlaysChanged, CancellationToken.None)
                is BlokeBot.Eventing.ObserverFanOutOutcome.CompletedWithFailures;
        }
        catch (Exception exception)
        {
            notificationPending = true;
            logger.LogWarning(
                "Full overlay import committed; notification needs follow-up ({FailureType}).",
                exception.GetType().FullName
            );
        }
        return Success(
            new FullOverlayImportApplied(
                new(ToView(created.Row), created.Access),
                notificationPending
            )
        );
    }
}
