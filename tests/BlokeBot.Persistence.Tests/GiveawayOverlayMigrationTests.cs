using System.Data.Common;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class GiveawayOverlayMigrationTests
{
    private const string _previousMigration = "20260731064005_v0.6.0_CustomCommandOverlayCues";
    private const string _latestMigration = "20260731083003_v0.6.0_GiveawayOverlay";

    [Test]
    public async Task Upgrade_PreservesExistingOverlayTypesAndAllowsGiveawayRows()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                     CommandsAliasesConfigured, CreatedAtUtc)
                VALUES
                    (1, 'host-id', 'host', 'Host', 0, 511, 0, '2026-07-31T00:00:00Z');

                INSERT INTO overlay_instances
                    (PublicId, HostId, Name, Type, IsEnabled, ConfigurationJson,
                     AccessKeyDigest, KeyVersion, Revision, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('41ea1646-0cdf-4f22-a68c-d51afdd3d850', 1, 'Existing cue', 'cue-player', 1,
                     '{{"schemaVersion":1}}', zeroblob(32), 1, 1,
                     '2026-07-31T00:00:00Z', '2026-07-31T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        upgraded.GetService<IMigrationsAssembly>().Migrations.Count.ShouldBe(18);
        (await upgraded.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(_latestMigration);
        (await upgraded.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await upgraded.OverlayInstances.SingleAsync()).Type.ShouldBe(OverlayType.CuePlayer);

        upgraded.OverlayInstances.Add(
            new OverlayInstance
            {
                PublicId = Guid.Parse("805f4686-d192-4b9d-8481-790fef956a98"),
                HostId = 1,
                Name = "Giveaway",
                Type = OverlayType.Giveaway,
                IsEnabled = true,
                ConfigurationJson =
                    """{"schemaVersion":1,"title":"Community giveaway","showEntrantCount":true,"showCountdown":true,"showJoinCommand":true}""",
                AccessKeyDigest = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        await upgraded.SaveChangesAsync();

        (
            await upgraded
                .OverlayInstances.OrderBy(value => value.Id)
                .Select(value => value.Type)
                .ToArrayAsync()
        ).ShouldBe([OverlayType.CuePlayer, OverlayType.Giveaway]);
        (await ReadOverlayTableSqlAsync(upgraded.Database.GetDbConnection())).ShouldContain(
            "Type IN ('cue-player', 'empty', 'giveaway', 'guessing')"
        );
    }

    private static async Task<string> ReadOverlayTableSqlAsync(DbConnection connection)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'overlay_instances';""";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
