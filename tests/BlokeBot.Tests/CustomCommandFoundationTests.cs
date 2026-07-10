using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CustomCommandFoundationTests
{
    [Test]
    public async Task Custom_alias_registry_rejects_builtin_and_custom_collisions()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            var entry = MessageEntry(hostId, "hello");
            db.CustomMessageLibraryEntries.Add(entry);
            await db.SaveChangesAsync();
            var command = new CustomCommand
            {
                HostId = hostId,
                Name = "Hello",
                Action = new MessageCustomCommandAction
                {
                    HostId = hostId,
                    MessageLibraryEntryId = entry.Id,
                },
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            db.CustomCommands.Add(command);
            await db.SaveChangesAsync();
            db.CustomCommandAliases.Add(
                new CustomCommandAlias
                {
                    HostId = hostId,
                    CustomCommandId = command.Id,
                    Alias = "hello",
                }
            );
            await db.SaveChangesAsync();
        }

        var registry = new CustomCommandAliasRegistry();
        await using var verify = await dbFactory.CreateDbContextAsync();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            registry.ValidateAsync(verify, hostId, null, "!POINTS", CancellationToken.None)
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            registry.ValidateAsync(verify, hostId, null, "hello", CancellationToken.None)
        );
    }

    [Test]
    public async Task Host_timezone_service_validates_and_persists_timezone()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostCustomCommandSettingsService(
            dbFactory,
            new EventBus<AppEventKind>()
        );

        await service.SetTimeZoneIdAsync(hostId, "UTC", CancellationToken.None);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SetTimeZoneIdAsync(hostId, "Missing/Zone", CancellationToken.None)
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var timeZone = await db
            .Hosts.Where(x => x.Id == hostId)
            .Select(x => x.TimeZoneId)
            .SingleAsync(CancellationToken.None);
        timeZone.ShouldBe("UTC");
    }

    [Test]
    public async Task Durable_alerts_are_created_once_and_acknowledged_with_actor()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var alerts = new DurableAlertService(dbFactory, clock, new EventBus<AppEventKind>());

        var first = await alerts.CreateAsync(
            hostId,
            DurableAlertSeverity.Warning,
            "queue",
            "streamer",
            "Queue delayed",
            "Outbound messages are delayed.",
            "/alerts",
            CancellationToken.None
        );
        var duplicate = await alerts.CreateAsync(
            hostId,
            DurableAlertSeverity.Warning,
            "queue",
            "streamer",
            "Queue delayed",
            "Outbound messages are delayed.",
            "/alerts",
            CancellationToken.None
        );

        duplicate.Id.ShouldBe(first.Id);
        (await alerts.CountActiveAsync(hostId, CancellationToken.None)).ShouldBe(1);

        var acknowledged = await alerts.AcknowledgeAsync(
            hostId,
            first.Id,
            "moderator",
            CancellationToken.None
        );

        acknowledged.ShouldBeTrue();
        (await alerts.CountActiveAsync(hostId, CancellationToken.None)).ShouldBe(0);
        await using var db = await dbFactory.CreateDbContextAsync();
        var stored = await db.DurableAlerts.SingleAsync(CancellationToken.None);
        stored.AcknowledgedByLogin.ShouldBe("moderator");
        stored.AcknowledgedAtUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
    }

    private static CustomMessageLibraryEntry MessageEntry(int hostId, string name) =>
        new()
        {
            HostId = hostId,
            Name = name,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Variants =
            [
                new CustomMessageVariant
                {
                    SortOrder = 0,
                    Text = "Hello {user}.",
                },
            ],
        };

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
