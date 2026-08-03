using BlokeBot.Core.Features.Alerts;
using BlokeBot.Persistence.Models;
using Shouldly;
using static BlokeBot.Core.Tests.PublicChatIntegrationTestSupport;

namespace BlokeBot.Core.Tests;

public sealed class FollowerOnlyChatAlertIntegrationTests
{
    [Test]
    public async Task OtherProviderCodes_ObservingTerminalRejection_CreateNoFollowerOnlyAlert()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var alerts = new DurableAlertService(
            dbFactory,
            new ManualTestTimeProvider(Utc(12, 0, 0)),
            TestEventBus.Create<AppEventKind>()
        );
        var observer = new DurableFollowerOnlyChatAlertObserver(dbFactory, alerts);

        await observer.TerminalRejectionAsync(
            new PublicChatTerminalRejection("streamer", "Followers_Only"),
            CancellationToken.None
        );
        await observer.TerminalRejectionAsync(
            new PublicChatTerminalRejection("streamer", "subscriber_only"),
            CancellationToken.None
        );

        (await alerts.LoadStateAsync(hostId, CancellationToken.None)).Active.ShouldBeEmpty();
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            CreatedAtUtc = Utc(12, 0, 0).UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
