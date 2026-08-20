using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bounties;

internal sealed class BountyPauseObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider
)
    : IHostFeatureChangeObserver,
        BlokeBot.Core.Features.ConfigurationTransfer.IConfigurationActivationObserver
{
    public async ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (feature is not (HostFeatureFlags.Bounties or HostFeatureFlags.Points))
        {
            return;
        }

        await ReconcileAsync(hostId, cancellationToken);
    }

    public ValueTask FeatureEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken cancellationToken
    ) =>
        feature is HostFeatureFlags.Bounties or HostFeatureFlags.Points
            ? new(ReconcileAsync(hostId, cancellationToken))
            : ValueTask.CompletedTask;

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
            await ReconcileAsync(hostId, ct);
        }
    }

    private async Task ReconcileAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(value => value.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var required = HostFeatureFlags.Bounties | HostFeatureFlags.Points;
        var effective = (host.EnabledFeatures & required) == required;
        if (!effective)
        {
            host.BountiesPausedAtUtc ??= now;
            _ = await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }

        if (host.BountiesPausedAtUtc is not { } pausedAt)
        {
            return;
        }

        var pausedFor = now - pausedAt;
        if (pausedFor > TimeSpan.Zero)
        {
            var active = await db
                .Bounties.Where(value =>
                    value.HostId == hostId
                    && (
                        value.Status == BountyStatus.Funding
                        || value.Status == BountyStatus.Accepted
                    )
                )
                .ToListAsync(ct);
            foreach (var bounty in active)
            {
                bounty.ExpiresAtUtc = bounty.ExpiresAtUtc.Add(pausedFor);
            }
        }

        host.BountiesPausedAtUtc = null;
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
