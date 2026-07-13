using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels;

public sealed class HostFeatureService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes
)
{
    public async Task<IReadOnlyDictionary<int, HostFeatureFlags>> LoadHostedFeaturesAsync(
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .Select(x => new { x.Id, x.EnabledFeatures })
            .ToArrayAsync(ct);

        return hosts.ToDictionary(x => x.Id, x => x.EnabledFeatures);
    }

    public async Task<HostFeatureFlags?> LoadAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == hostId)
            .Select(x => (HostFeatureFlags?)x.EnabledFeatures)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> IsEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken ct
    )
    {
        var features = await LoadAsync(hostId, ct);
        return features?.Contains(feature) == true;
    }

    public Task EnableAsync(int hostId, HostFeatureFlags feature, CancellationToken ct)
    {
        return UpdateAsync(hostId, feature, static (current, selected) => current | selected, ct);
    }

    public Task DisableAsync(int hostId, HostFeatureFlags feature, CancellationToken ct)
    {
        return UpdateAsync(hostId, feature, static (current, selected) => current & ~selected, ct);
    }

    private async Task UpdateAsync(
        int hostId,
        HostFeatureFlags feature,
        Func<HostFeatureFlags, HostFeatureFlags, HostFeatureFlags> update,
        CancellationToken ct
    )
    {
        if (feature is HostFeatureFlags.None or HostFeatureFlags.All)
        {
            throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        host.EnabledFeatures = update(host.EnabledFeatures, feature);

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }
}
