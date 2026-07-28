using BlokeBot.Core.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations;

public sealed class NativeTwitchFeatureGate(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : INativeTwitchFeatureStateProvider
{
    public const string DisabledMessage =
        "Native Twitch tools are turned off for the selected channel.";

    public async Task<bool> IsEnabledAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId)
            .Select(host =>
                (host.EnabledFeatures & HostFeatureFlags.NativeTwitch)
                == HostFeatureFlags.NativeTwitch
            )
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async ValueTask<bool> IsEnabledAsync(string channel, CancellationToken cancellationToken)
    {
        var login = LoginName.Parse(channel);
        if (login.IsEmpty)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == login.Value)
            .Select(host =>
                (host.EnabledFeatures & HostFeatureFlags.NativeTwitch)
                == HostFeatureFlags.NativeTwitch
            )
            .SingleOrDefaultAsync(cancellationToken);
    }
}
