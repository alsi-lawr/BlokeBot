using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels;

public sealed class HostFeatureService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes,
    IEnumerable<INativeTwitchFeatureChangeObserver> nativeTwitchObservers
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

    public IO<Option<HostFeatureFlags>, Never> Load(int hostId)
    {
        return IO<Option<HostFeatureFlags>, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var features = await db
                .Hosts.AsNoTracking()
                .Where(x => x.Id == hostId)
                .Select(x => (HostFeatureFlags?)x.EnabledFeatures)
                .SingleOrDefaultAsync(ct);
            return Result<Option<HostFeatureFlags>, Never>.Success(
                features.HasValue
                    ? Option<HostFeatureFlags>.Some(features.Value)
                    : Option<HostFeatureFlags>.None
            );
        });
    }

    public async Task<bool> IsEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken ct
    )
    {
        var result = await Load(hostId).ExecuteAsync(ct);
        return result.Match(
            features => features.Match(value => value.Contains(feature), () => false),
            _ => throw new UnreachableException()
        );
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

        var updated = update(host.EnabledFeatures, feature);
        if (updated == host.EnabledFeatures)
        {
            return;
        }

        host.EnabledFeatures = updated;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        if (feature is not HostFeatureFlags.NativeTwitch)
        {
            return;
        }

        var state = updated.Contains(HostFeatureFlags.NativeTwitch)
            ? NativeTwitchFeatureState.Enabled
            : NativeTwitchFeatureState.Disabled;
        foreach (var observer in nativeTwitchObservers)
        {
            await observer.NativeTwitchFeatureChangedAsync(hostId, state, ct);
        }
    }
}
