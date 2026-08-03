using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class GuessingOverlayMigrationTests
{
    private const string _previousMigration = "20260730202307_v0.5.0_IndependentChatTools";
    private const string _guessingMigration = "20260731015218_v0.6.0_GuessingOverlay";

    [Test]
    public async Task Upgrade_PreservesEmptyOverlaysAndAllowsTypedGuessingRows()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'host-id', 'host', 'Host', 0, 17, 0, '2026-07-31T00:00:00Z');

                INSERT INTO overlay_instances
                    (PublicId, HostId, Name, Type, IsEnabled, ConfigurationJson,
                     AccessKeyDigest, KeyVersion, Revision, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('41ea1646-0cdf-4f22-a68c-d51afdd3d850', 1, 'Existing empty', 'empty', 1,
                     '{{"schemaVersion":1}}', zeroblob(32), 1, 1,
                     '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        (await upgraded.Database.GetAppliedMigrationsAsync()).ShouldContain(_guessingMigration);
        (await upgraded.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await upgraded.OverlayInstances.SingleAsync()).Type.ShouldBe(OverlayType.Empty);

        _ = upgraded.OverlayInstances.Add(
            new OverlayInstance
            {
                PublicId = Guid.Parse("805f4686-d192-4b9d-8481-790fef956a98"),
                HostId = 1,
                Name = "Guessing",
                Type = OverlayType.Guessing,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"showGuessCount":true,"resultDurationSeconds":8}""",
                AccessKeyDigest = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await upgraded.SaveChangesAsync();

        (
            await upgraded
                .OverlayInstances.OrderBy(value => value.Id)
                .Select(value => value.Type)
                .ToArrayAsync()
        ).ShouldBe([OverlayType.Empty, OverlayType.Guessing]);
    }
}
