using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayEventFeedService
{
    private static async Task<EventFeedStatePresentation> ReadStateAsync(
        BlokeBotDbContext db,
        int hostId,
        long overlayId,
        CancellationToken ct
    )
    {
        var items = await db
            .OverlayEventFeedItems.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && x.OverlayInstanceId == overlayId
                && (
                    x.Lifecycle == OverlayEventFeedLifecycle.Active
                    || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
            )
            .OrderBy(x => x.EnqueuedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return new EventFeedStatePresentation(
            items
                .Where(x => x.Lifecycle == OverlayEventFeedLifecycle.Active)
                .Select(ToPresentation)
                .SingleOrDefault(),
            items
                .Where(x => x.Lifecycle == OverlayEventFeedLifecycle.Queued)
                .Select(ToPresentation)
                .ToArray()
        );
    }

    private async Task AdvanceAsync(
        BlokeBotDbContext db,
        ResolvedOverlayInstance instance,
        CancellationToken ct
    )
    {
        var overlay = await db.OverlayInstances.SingleAsync(
            x => x.PublicId == instance.OverlayId && x.HostId == instance.HostId,
            ct
        );
        _ = await PruneAndAdvanceAsync(db, overlay, ct);
    }
}
