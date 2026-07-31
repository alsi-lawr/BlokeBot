using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

public sealed class OverlayInstanceResolver(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<OverlayResolutionResult> ResolveAsync(string accessKey, CancellationToken ct)
    {
        if (
            string.IsNullOrWhiteSpace(accessKey)
            || !OverlayAccessKeyDigest.HasCanonicalShape(accessKey)
        )
        {
            return new OverlayResolutionResult.NotFound();
        }

        var digest = OverlayAccessKeyDigest.Compute(accessKey);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var resolved = await db
            .OverlayInstances.AsNoTracking()
            .Where(value => value.IsEnabled && value.AccessKeyDigest == digest)
            .Join(
                db.Hosts.AsNoTracking(),
                overlay => overlay.HostId,
                host => host.Id,
                (overlay, host) => new { Overlay = overlay, host.EnabledFeatures }
            )
            .SingleOrDefaultAsync(ct);
        if (
            resolved is null
            || !OverlayRequiredFeatures.AreEnabled(resolved.Overlay.Type, resolved.EnabledFeatures)
        )
        {
            return new OverlayResolutionResult.NotFound();
        }

        var overlay = resolved.Overlay;
        return new OverlayResolutionResult.Resolved(
            new ResolvedOverlayInstance(
                overlay.HostId,
                overlay.PublicId,
                overlay.Type,
                OverlayConfiguration.FromPersistence(overlay.Type, overlay.ConfigurationJson),
                new OverlayRevision(overlay.Revision)
            )
        );
    }
}
