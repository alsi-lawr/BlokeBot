using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService
{
    private static async Task<bool> FullOverlayReferencesMediaAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid assetId,
        CancellationToken ct
    )
    {
        var drafts = await db
            .FullOverlays.AsNoTracking()
            .Where(overlay => overlay.HostId == hostId)
            .Select(overlay => overlay.DraftDocumentJson)
            .ToArrayAsync(ct);
        var selected = await (
            from overlay in db.FullOverlays.AsNoTracking()
            join publication in db.FullOverlayPublications.AsNoTracking()
                on new { OverlayId = overlay.Id, Version = overlay.PublishedVersion } equals new
                {
                    publication.OverlayId,
                    Version = (long?)publication.Version,
                }
            where overlay.HostId == hostId
            select publication.DocumentJson
        ).ToArrayAsync(ct);
        return drafts
            .Concat(selected)
            .Any(json =>
                FullOverlayDocuments
                    .Deserialize(json)
                    .Widgets.Any(widget =>
                        widget.Kind.Value is "image" or "audio" or "video"
                        && FullOverlayWidgetConfigurations.Read<FullOverlayMediaConfiguration>(
                            widget.Configuration
                        )
                            is { } reference
                        && reference.AssetId == assetId
                    )
            );
    }
}
