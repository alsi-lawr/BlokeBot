using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CommunityProgressionUiTests
{
    [Test]
    public async Task SignedInDirectRoute_WhenDisabled_ShowsRecoveryWithoutRetainedData()
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
        using var context = UiTestContextFactory.Create(database, hostId);
        _ = context.Services.AddSingleton(service);

        var cut = context.Render<CommunityProgressionPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Community progression is off for this channel");
            cut.Markup.ShouldContain("/host#chat-tools");
            cut.Markup.ShouldContain("retained");
            cut.Markup.ShouldNotContain("RETAINED-SEASON");
        });
    }
}
