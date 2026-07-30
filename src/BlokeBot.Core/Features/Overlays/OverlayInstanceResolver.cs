using BlokeBot.Persistence;
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
        var overlay = await db
            .OverlayInstances.AsNoTracking()
            .SingleOrDefaultAsync(value => value.IsEnabled && value.AccessKeyDigest == digest, ct);
        if (overlay is null)
        {
            return new OverlayResolutionResult.NotFound();
        }

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
