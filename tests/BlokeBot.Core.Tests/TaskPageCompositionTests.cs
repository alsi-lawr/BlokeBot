using BlokeBot.Core.Features.Home;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Persistence.Models;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TaskPageCompositionTests
{
    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
