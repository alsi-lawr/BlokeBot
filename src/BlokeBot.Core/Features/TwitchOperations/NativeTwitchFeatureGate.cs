using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations;

public sealed class NativeTwitchFeatureGate(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : INativeTwitchFeatureStateProvider
{
    public const string DisabledMessage = "This tool is turned off for the selected channel.";

    public async Task<bool> IsEnabledAsync(
        int hostId,
        HostFeatureFlags feature,
        CancellationToken cancellationToken
    )
    {
        EnsureNativeFeature(feature);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId)
            .Select(host => (host.EnabledFeatures & feature) == feature)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<bool> IsEnabledAsync(
        string channel,
        NativeTwitchFeature feature,
        CancellationToken cancellationToken
    ) => await IsEnabledAsync(channel, Map(feature), cancellationToken);

    public async ValueTask<bool> IsEnabledAsync(
        string channel,
        HostFeatureFlags feature,
        CancellationToken cancellationToken
    )
    {
        EnsureNativeFeature(feature);
        var login = LoginName.Parse(channel);
        if (login.IsEmpty)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == login.Value)
            .Select(host => (host.EnabledFeatures & feature) == feature)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static HostFeatureFlags Map(NativeTwitchFeature feature) =>
        feature switch
        {
            NativeTwitchFeature.Shoutouts => HostFeatureFlags.Shoutouts,
            NativeTwitchFeature.Polls => HostFeatureFlags.Polls,
            NativeTwitchFeature.RewardsAndRedemptions => HostFeatureFlags.RewardsAndRedemptions,
            NativeTwitchFeature.Predictions => HostFeatureFlags.Predictions,
            NativeTwitchFeature.RaidCollaboration => HostFeatureFlags.RaidCollaboration,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };

    private static void EnsureNativeFeature(HostFeatureFlags feature)
    {
        if (
            feature
            is not (
                HostFeatureFlags.Shoutouts
                or HostFeatureFlags.Polls
                or HostFeatureFlags.ClipsAndMarkers
                or HostFeatureFlags.RewardsAndRedemptions
                or HostFeatureFlags.Predictions
                or HostFeatureFlags.Moments
                or HostFeatureFlags.RaidCollaboration
            )
        )
        {
            throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }
    }
}
