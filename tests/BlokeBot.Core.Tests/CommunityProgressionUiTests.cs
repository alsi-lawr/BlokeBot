using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CommunityProgressionUiTests
{
    [Test]
    public async Task DisabledHost_SeasonProjectionsExposeNoRetainedData()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        int hostId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = new BotHost
            {
                TwitchUserId = "streamer-id",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.CommunityProgression,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            hostId = host.Id;
        }
        var service = new CommunityProgressionService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        _ = (
            await service.CreateSeasonAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    "RETAINED-SEASON",
                    "retained description",
                    "private note",
                    CommunityVisibility.Public,
                    DateTime.UtcNow.AddDays(-1),
                    DateTime.UtcNow.AddDays(30),
                    new("streamer-id", "streamer")
                ),
                default
            )
        ).ShouldBeOfType<CommunityOperationOutcome.Succeeded>();
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync();
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        (await service.GetModeratorSeasonsAsync(hostId, default)).ShouldBeEmpty();
        (await service.GetPublicAsync("streamer", default)).ShouldBeNull();
    }
}
