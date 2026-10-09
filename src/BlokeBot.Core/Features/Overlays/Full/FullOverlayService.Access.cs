using BlokeBot.Core.Auth.Sessions;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed partial class FullOverlayService
{
    internal async Task<FullOverlayResult<FullOverlayPrivateAccess>> ReadAccessAsync(
        AuthenticatedSession session,
        Guid overlayId,
        CancellationToken ct
    )
    {
        var authorization = await AuthorizeAsync(session, ct);
        if (authorization is FullOverlayResult<OverlayManagementActor>.Rejected rejected)
        {
            return Reject<FullOverlayPrivateAccess>(rejected.Reason);
        }
        var actor = ((FullOverlayResult<OverlayManagementActor>.Succeeded)authorization).Value;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db
            .FullOverlays.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == actor.HostId && value.PublicId == overlayId,
                ct
            );
        if (row is null)
        {
            return Reject<FullOverlayPrivateAccess>(FullOverlayRejectionKind.NotFound);
        }
        var access = keyProtection.Read(actor.HostId, overlayId, row.ProtectedAccessKey);
        var changed = await ReauthorizeAsync(session, actor, ct);
        return changed is not null ? Reject<FullOverlayPrivateAccess>(changed)
            : access is null
                ? Reject<FullOverlayPrivateAccess>(FullOverlayRejectionKind.AccessUnavailable)
            : Success(access);
    }
}
