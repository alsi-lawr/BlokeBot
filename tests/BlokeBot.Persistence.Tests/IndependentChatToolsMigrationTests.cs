using System.Data.Common;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class IndependentChatToolsMigrationTests
{
    private const string _viewerCommandCatalog = "20260730162013_v0.5.0_ViewerCommandCatalog";
    private const string _independentChatTools = "20260730202307_v0.5.0_IndependentChatTools";

    [Test]
    public async Task Upgrade_PreservesExistingBehaviorAndUnknownBitsWhileFreshHostsOptIn()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var before = await factory.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_viewerCommandCatalog);
            _ = await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO hosts
                    (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures, CreatedAtUtc)
                VALUES
                    (1, 'zero-id', 'zero', 'zero', 0, 0, '2026-07-30T00:00:00Z'),
                    (2, 'coarse-id', 'coarse', 'coarse', 0, 8, '2026-07-30T00:00:00Z'),
                    (3, 'existing-id', 'existing', 'existing', 0, 23, '2026-07-30T00:00:00Z'),
                    (4, 'unknown-id', 'unknown', 'unknown', 0, 4104, '2026-07-30T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        (
            await upgraded
                .Hosts.OrderBy(static value => value.Id)
                .Select(static value => (long)value.EnabledFeatures)
                .ToArrayAsync()
        ).ShouldBe([224L, 4072L, 247L, 8168L]);
        (await ReadDefaultAsync(upgraded.Database.GetDbConnection())).ShouldBe("0");
        (await ReadMigrationsAsync(upgraded.Database.GetDbConnection())).ShouldContain(
            _independentChatTools
        );

        var fresh = new BotHost
        {
            TwitchUserId = "fresh-id",
            Login = "fresh",
            DisplayName = "fresh",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = upgraded.Hosts.Add(fresh);
        _ = await upgraded.SaveChangesAsync();
        fresh.EnabledFeatures.ShouldBe(HostFeatureFlags.None);
    }

    [Test]
    public async Task Down_EnablesLegacyCoarseBitOnlyForAllFiveChildren()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var latest = await factory.CreateDbContextAsync())
        {
            await latest.Database.MigrateAsync();
            latest.Hosts.AddRange(
                Host("all", HostFeatureFlags.All),
                Host("none", HostFeatureFlags.None),
                Host("one", HostFeatureFlags.Shoutouts),
                Host(
                    "four",
                    HostFeatureFlags.Shoutouts
                        | HostFeatureFlags.Polls
                        | HostFeatureFlags.ClipsAndMarkers
                        | HostFeatureFlags.RewardsAndRedemptions
                ),
                Host("unknown", HostFeatureFlags.NativeTwitchFeatures | (HostFeatureFlags)4096UL)
            );
            _ = await latest.SaveChangesAsync();
            await latest.GetService<IMigrator>().MigrateAsync(_viewerCommandCatalog);
        }

        await using var downgraded = await factory.CreateDbContextAsync();
        (
            await downgraded
                .Hosts.OrderBy(static value => value.Id)
                .Select(static value => (long)value.EnabledFeatures)
                .ToArrayAsync()
        ).ShouldBe([31L, 0L, 0L, 0L, 4104L]);
        (await ReadDefaultAsync(downgraded.Database.GetDbConnection())).ShouldBe("31");
    }

    private static BotHost Host(string login, HostFeatureFlags features) =>
        new()
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = features,
            CreatedAtUtc = DateTime.UtcNow,
        };

    private static async Task<string> ReadDefaultAsync(DbConnection connection)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT "dflt_value" FROM pragma_table_info('hosts') WHERE "name" = 'EnabledFeatures';""";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<string>> ReadMigrationsAsync(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";""";
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
