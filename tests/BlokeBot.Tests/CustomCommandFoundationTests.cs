using BlokeBot.Eventing;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CustomCommandFoundationTests
{
    [Test]
    public async Task BuiltInOrExistingCustomAlias_ValidatingCustomAlias_RejectsCollision()
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
    public async Task ValidThenInvalidTimeZone_SavingHostSettings_PersistsOnlyValidZone()
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
}
