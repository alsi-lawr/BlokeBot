using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class EventFeedOverlayMigrationTests
{
    [Test]
    public async Task LatestSchema_PersistsIsolatedUniqueBoundedFeedRows()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var db = await factory.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "host-id",
            Login = "host",
            DisplayName = "Host",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var overlay = new OverlayInstance
        {
            PublicId = Guid.NewGuid(),
            HostId = host.Id,
            Name = "Feed",
            Type = OverlayType.EventFeed,
            IsEnabled = true,
            ConfigurationJson =
                """{"schemaVersion":1,"capacity":10,"overflowPolicy":"dropNewest","kinds":{"pointAward":{"enabled":true,"template":"{recipient} received {amount} {pointLabel}","priority":"normal","durationSeconds":6},"guessingWinner":{"enabled":true,"template":"{winners} won {roundName}: {winningAnswer}","priority":"high","durationSeconds":8},"giveawayWinner":{"enabled":true,"template":"{winners} won {prizes}","priority":"high","durationSeconds":8}}}""",
            AccessKeyDigest = new byte[32],
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _ = db.OverlayInstances.Add(overlay);
        _ = await db.SaveChangesAsync();
        _ = db.OverlayEventFeedItems.Add(
            new OverlayEventFeedItem
            {
                OverlayInstanceId = overlay.Id,
                HostId = host.Id,
                Kind = OverlayEventFeedKind.PointAward,
                SourceKey = "ledger-1",
                Priority = OverlayEventFeedPriority.Normal,
                Lifecycle = OverlayEventFeedLifecycle.Active,
                Title = "Points awarded",
                Body = string.Concat(Enumerable.Repeat("&lt;viewer&gt; received 5 points ", 30)),
                DurationSeconds = 6,
                EnqueuedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        var persisted = await db.OverlayEventFeedItems.SingleAsync();
        persisted.SourceKey.ShouldBe("ledger-1");
        persisted.Body.Length.ShouldBeGreaterThan(500);
        persisted.DurationSeconds.ShouldBe(6);

        _ = db.OverlayEventFeedItems.Add(
            new OverlayEventFeedItem
            {
                OverlayInstanceId = overlay.Id,
                HostId = host.Id,
                Kind = OverlayEventFeedKind.PointAward,
                SourceKey = "ledger-1",
                Priority = OverlayEventFeedPriority.Normal,
                Lifecycle = OverlayEventFeedLifecycle.Queued,
                Title = "Points awarded",
                Body = "duplicate",
                DurationSeconds = 6,
                EnqueuedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
