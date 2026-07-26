using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class ShoutoutServiceTests
{
    [Test]
    public async Task DuplicateProviderDelivery_RecordingShoutout_UpdatesOnlyMatchingHostOnce()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Hosts.AddRange(
                new BotHost
                {
                    Login = "first",
                    DisplayName = "First",
                    TwitchUserId = "first-id",
                },
                new BotHost
                {
                    Login = "second",
                    DisplayName = "Second",
                    TwitchUserId = "second-id",
                }
            );
            await db.SaveChangesAsync();
        }
        var service = new ShoutoutService(
            dbFactory,
            null!,
            null!,
            null!,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        var delivery = new EventSubShoutoutEvent(
            "first-id",
            "first",
            "source-id",
            "source",
            "target-id",
            "target",
            42,
            DateTimeOffset.Parse("2026-07-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T01:00:00Z"),
            DateTimeOffset.Parse("2026-07-26T02:00:00Z"),
            EventSubShoutoutDirection.Sent,
            "provider-delivery"
        );

        await service.ShoutoutReceivedAsync(delivery, CancellationToken.None);
        await service.ShoutoutReceivedAsync(delivery, CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var first = await verify.ShoutoutHistory.Where(x => x.HostId == 1).ToArrayAsync();
        first.Length.ShouldBe(1);
        (await verify.ShoutoutHistory.Where(x => x.HostId == 2).CountAsync()).ShouldBe(0);
        first
            .Single()
            .TargetCooldownEndsAtUtc.ShouldBe(
                DateTime.Parse("2026-07-26T02:00:00Z").ToUniversalTime()
            );
    }
}
