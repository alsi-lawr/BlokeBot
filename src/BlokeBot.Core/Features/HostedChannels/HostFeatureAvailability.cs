using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels;

internal static class HostFeatureAvailability
{
    public static async Task<bool> IsEnabledAsync(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        int hostId,
        HostFeatureFlags feature,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId)
            .Select(host => (host.EnabledFeatures & feature) == feature)
            .SingleOrDefaultAsync(ct);
    }

    public static async Task<bool> IsEnabledAsync(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        string hostLogin,
        HostFeatureFlags feature,
        CancellationToken ct
    )
    {
        var login = hostLogin.Trim().TrimStart('@').ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(host => host.Login == login)
            .Select(host => (host.EnabledFeatures & feature) == feature)
            .SingleOrDefaultAsync(ct);
    }
}
