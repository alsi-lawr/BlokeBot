using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bounties;

internal sealed class BountyPauseObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BountyService bountyService
) : IHostFeatureActivationObserver
{
    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (change.Feature is not (HostFeatureFlags.Bounties or HostFeatureFlags.Points))
        {
            return new HostFeatureAutomaticWorkResult.Complete();
        }

        await bountyService.ReconcilePauseAsync(
            change.HostId,
            BountyPauseRecoveryCause.FeatureChanged(change.Feature, change.State),
            cancellationToken
        );
        return new HostFeatureAutomaticWorkResult.Complete();
    }

    internal async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var required = HostFeatureFlags.Bounties | HostFeatureFlags.Points;
        var hostIds = await db
            .Hosts.AsNoTracking()
            .Where(host =>
                host.BountiesPausedAtUtc != null && (host.EnabledFeatures & required) == required
            )
            .Select(host => host.Id)
            .ToListAsync(ct);
        foreach (var hostId in hostIds)
        {
            await bountyService.ReconcilePauseAsync(hostId, BountyPauseRecoveryCause.Restart(), ct);
        }
    }
}
