using BlokeBot.Features.PublicLeaderboards;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PublicLeaderboardTests
{
    [Test]
    public async Task PrefixedMixedCaseChannel_LookingUpLeaderboardHost_ReturnsNormalizedHost()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.Add(
                new BotHost
                {
                    Login = "streamer",
                    DisplayName = "Streamer",
                    CreatedAtUtc = DateTime.UtcNow,
                    EnabledFeatures = HostFeatureFlags.Points,
                }
            );
            await db.SaveChangesAsync();
        }
        var lookup = new PublicLeaderboardHostLookup(dbFactory);

        var host = await lookup.FindAsync("@Streamer", CancellationToken.None);

        host.ShouldNotBeNull();
        host.Login.ShouldBe("streamer");
        host.DisplayName.ShouldBe("Streamer");
        host.EnabledFeatures.ShouldBe(HostFeatureFlags.Points);
    }

    [Test]
    public async Task UnknownChannel_LookingUpLeaderboardHost_ReturnsNull()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var lookup = new PublicLeaderboardHostLookup(dbFactory);

        var host = await lookup.FindAsync("missing", CancellationToken.None);

        host.ShouldBeNull();
    }
}
