using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays.Full;

internal readonly record struct FullOverlayGeneration(long Value);

internal sealed record PublishedFullOverlay(
    int HostId,
    Guid OverlayId,
    FullOverlayVersion Version,
    FullOverlayRevision Revision,
    FullOverlayGeneration Generation,
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
        return await ResolveAsync(
            db,
            db.FullOverlays.Where(row => row.AccessKeyDigest == digest),
            ct
        );
    }

    internal async Task<PublishedFullOverlay?> ResolveAsync(
        int hostId,
        Guid overlayId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ResolveAsync(
            db,
            db.FullOverlays.Where(row => row.HostId == hostId && row.PublicId == overlayId),
            ct
        );
    }

    private static async Task<PublishedFullOverlay?> ResolveAsync(
        BlokeBotDbContext db,
        IQueryable<FullOverlay> overlays,
        CancellationToken ct
    )
    {
        var selected = await (
            from overlay in overlays.AsNoTracking()
            join host in db.Hosts.AsNoTracking() on overlay.HostId equals host.Id
            join publication in db.FullOverlayPublications.AsNoTracking()
                on new { OverlayId = overlay.Id, Version = overlay.PublishedVersion } equals new
                {
                    publication.OverlayId,
                    Version = (long?)publication.Version,
                }
            where
                !overlay.IsArchived
                && (host.EnabledFeatures & HostFeatureFlags.Overlays) == HostFeatureFlags.Overlays
            select new
            {
                overlay.HostId,
                overlay.PublicId,
                overlay.Revision,
                overlay.PublicationSequence,
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
                new(selected.PublicationSequence),
                FullOverlayDocuments.Deserialize(selected.DocumentJson) with
                {
                    Diagnostics = [],
                }
            );
    }
}
