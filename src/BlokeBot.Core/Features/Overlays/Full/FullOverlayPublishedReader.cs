using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed record PublishedFullOverlay(
    int HostId,
    Guid OverlayId,
    FullOverlayVersion Version,
    FullOverlayRevision Revision,
    FullOverlayDocument Document
);

internal sealed class FullOverlayPublishedReader(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<PublishedFullOverlay?> ResolveAsync(string accessKey, CancellationToken ct)
    {
        if (!OverlayAccessKeyDigest.HasCanonicalShape(accessKey))
        {
            return null;
        }
        var digest = OverlayAccessKeyDigest.Compute(accessKey);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var selected = await (
            from overlay in db.FullOverlays.AsNoTracking()
            join host in db.Hosts.AsNoTracking() on overlay.HostId equals host.Id
            join publication in db.FullOverlayPublications.AsNoTracking()
                on new { OverlayId = overlay.Id, Version = overlay.PublishedVersion } equals new
                {
                    publication.OverlayId,
                    Version = (long?)publication.Version,
                }
            where
                !overlay.IsArchived
                && overlay.AccessKeyDigest == digest
                && (host.EnabledFeatures & HostFeatureFlags.Overlays) == HostFeatureFlags.Overlays
            select new
            {
                overlay.HostId,
                overlay.PublicId,
                overlay.Revision,
                publication.Version,
                publication.DocumentJson,
            }
        ).SingleOrDefaultAsync(ct);
        return selected is null
            ? null
            : new(
                selected.HostId,
                selected.PublicId,
                new(selected.Version),
                new(selected.Revision),
                FullOverlayDocuments.Deserialize(selected.DocumentJson) with
                {
                    Diagnostics = [],
                }
            );
    }
}
