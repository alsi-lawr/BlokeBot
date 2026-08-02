using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class OverlayFeatureSwitchMigrationTests
{
    private const string _overlayInstances = "20260730084046_v0.5.0_OverlayInstances";
    private const string _overlayFeatureSwitch = "20260730141846_v0.5.0_OverlayFeatureSwitch";

    [Test]
    public async Task Upgrade_OrsOnlyOverlayBitAndFreshDefaultUsesCompleteMask()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_overlayInstances);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures, CreatedAtUtc)
                VALUES
                    (1, 'none-id', 'none', 'none', 0, 0, '2026-07-30T00:00:00Z'),
                    (2, 'custom-id', 'custom', 'custom', 0, 4, '2026-07-30T00:00:00Z'),
                    (3, 'unknown-id', 'unknown', 'unknown', 0, 64, '2026-07-30T00:00:00Z');
                """
            );
            await before.GetService<IMigrator>().MigrateAsync(_overlayFeatureSwitch);
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        var masks = await upgraded
            .Hosts.OrderBy(value => value.Id)
            .Select(value => (long)value.EnabledFeatures)
            .ToArrayAsync();
        masks.ShouldBe([16L, 20L, 80L]);
        (
            await ReadScalarAsync(
                upgraded.Database.GetDbConnection(),
                """SELECT "dflt_value" FROM pragma_table_info('hosts') WHERE "name" = 'EnabledFeatures';"""
            )
        ).ShouldBe("31");
        (
            await ReadColumnAsync(
                upgraded.Database.GetDbConnection(),
                """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";"""
            )
        ).ShouldContain(_overlayFeatureSwitch);
    }

    [Test]
    public async Task Down_RemovesOnlyOverlayBitAndRestoresPriorDefault()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var latest = await factory.CreateDbContextAsync())
        {
            await latest.GetService<IMigrator>().MigrateAsync(_overlayFeatureSwitch);
            await latest.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures, CreatedAtUtc)
                VALUES
                    (1, 'all-id', 'all', 'all', 0, 31, '2026-07-30T00:00:00Z'),
                    (2, 'unknown-id', 'unknown', 'unknown', 0, 80, '2026-07-30T00:00:00Z');
                """
            );
            await latest.GetService<IMigrator>().MigrateAsync(_overlayInstances);
        }

        await using var downgraded = await factory.CreateDbContextAsync();
        (
            await downgraded
                .Hosts.OrderBy(value => value.Id)
                .Select(value => (long)value.EnabledFeatures)
                .ToArrayAsync()
        ).ShouldBe([15L, 64L]);
        (
            await ReadScalarAsync(
                downgraded.Database.GetDbConnection(),
                """SELECT "dflt_value" FROM pragma_table_info('hosts') WHERE "name" = 'EnabledFeatures';"""
            )
        ).ShouldBe("15");
    }

    private static async Task<string> ReadScalarAsync(DbConnection connection, string sql)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<string>> ReadColumnAsync(
        DbConnection connection,
        string sql
    )
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
