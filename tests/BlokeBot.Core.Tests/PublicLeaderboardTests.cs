using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PublicLeaderboardTests
{
    [Test]
    public async Task PrefixedMixedCaseChannel_LookingUpLeaderboardHost_ReturnsNormalizedHost()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    Login = "streamer",
                    DisplayName = "Streamer",
                    CreatedAtUtc = DateTime.UtcNow,
                    EnabledFeatures = HostFeatureFlags.Points,
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var lookup = new PublicLeaderboardHostLookup(dbFactory);

        var host = (
            await lookup.Find("@Streamer").RunAsync(CancellationToken.None)
        ).Match<PublicLeaderboardHost?>(static value => value, static () => null);

        _ = host.ShouldNotBeNull();
        host.Login.ShouldBe("streamer");
        host.DisplayName.ShouldBe("Streamer");
        host.EnabledFeatures.ShouldBe(HostFeatureFlags.Points);
    }

    [Test]
    public async Task UnknownChannel_LookingUpLeaderboardHost_ReturnsNull()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var lookup = new PublicLeaderboardHostLookup(dbFactory);

        var host = (
            await lookup.Find("missing").RunAsync(CancellationToken.None)
        ).Match<PublicLeaderboardHost?>(static value => value, static () => null);

        host.ShouldBeNull();
    }
}
