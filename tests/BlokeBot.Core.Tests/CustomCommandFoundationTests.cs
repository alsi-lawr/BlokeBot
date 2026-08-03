using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandFoundationTests
{
    [Test]
    public async Task BuiltInOrExistingCustomAlias_ValidatingCustomAlias_RejectsCollision()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            var entry = MessageEntry(hostId, "hello");
            _ = db.CustomMessageLibraryEntries.Add(entry);
            _ = await db.SaveChangesAsync();
            var command = new CustomCommand
            {
                HostId = hostId,
                Name = "Hello",
                Action = new MessageCustomCommandAction
                {
                    HostId = hostId,
                    ZeroArgumentMessageLibraryEntryId = entry.Id,
                },
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _ = db.CustomCommands.Add(command);
            _ = await db.SaveChangesAsync();
            _ = db.CustomCommandAliases.Add(
                new CustomCommandAlias
                {
                    HostId = hostId,
                    CustomCommandId = command.Id,
                    Alias = "hello",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var registry = new CustomCommandAliasRegistry();
        await using var verify = await dbFactory.CreateDbContextAsync();

        var builtIn = await registry.FindConflictAsync(
            verify,
            hostId,
            new HashSet<int>(),
            ["points"],
            CancellationToken.None
        );
        var custom = await registry.FindConflictAsync(
            verify,
            hostId,
            new HashSet<int>(),
            ["hello"],
            CancellationToken.None
        );

        builtIn.ShouldBe(new CustomCommandAliasConflict.BuiltIn("points"));
        custom.ShouldBe(new CustomCommandAliasConflict.Custom("hello"));
    }

    [Test]
    public async Task InvalidTimeZone_Validating_RemainsTypedAndDoesNotChangeHostSettings()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new HostCustomCommandSettingsService(
            dbFactory,
            TestEventBus.Create<AppEventKind>()
        );

        var valid = CustomCommandConfigurationValidator
            .Validate(new CustomCommandConfiguration { TimeZoneId = "UTC" })
            .Match(
                command => command,
                _ => throw new InvalidOperationException("Expected valid time zone.")
            );
        await service.SetTimeZoneAsync(hostId, valid.TimeZone, CancellationToken.None);
        var invalid = CustomCommandConfigurationValidator
            .Validate(new CustomCommandConfiguration { TimeZoneId = "Missing/Zone" })
            .Match(
                _ => Array.Empty<CustomCommandConfigurationValidationError>(),
                errors => errors.ToArray()
            );

        invalid.ShouldContain(error => error.Message.Contains("Time zone 'Missing/Zone'"));

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
            Variants = [new CustomMessageVariant { SortOrder = 0, Text = "Hello {user}." }],
        };

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
