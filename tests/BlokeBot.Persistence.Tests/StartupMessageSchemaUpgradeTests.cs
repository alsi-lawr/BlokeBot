using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class StartupMessageSchemaUpgradeTests
{
    [Test]
    public async Task LegacyHostSchema_Initializing_AddsNullableOverrideWithoutChangingHost()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var setupDb = await dbFactory.CreateDbContextAsync())
        {
            await setupDb.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE hosts DROP COLUMN StartupMessageEnabled;
                ALTER TABLE hosts DROP COLUMN StartupMessageText;

                INSERT INTO hosts (Login, DisplayName, BotRuntimeState, CreatedAtUtc)
                VALUES ('legacy', 'Legacy', 0, '2026-07-22 12:00:00');
                """
            );
        }

        var initializer = new BlokeBotDatabaseInitializer(dbFactory);
        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var connection = (SqliteConnection)verifyDb.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Login, StartupMessageEnabled, StartupMessageText FROM hosts;";
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("legacy");
        reader.IsDBNull(1).ShouldBeTrue();
        reader.IsDBNull(2).ShouldBeTrue();
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task NewDatabase_Initializing_CreatesStartupMessageColumnsIdempotently()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        var initializer = new BlokeBotDatabaseInitializer(dbFactory);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(hosts);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }

        columns.ShouldContain("StartupMessageEnabled");
        columns.ShouldContain("StartupMessageText");
    }
}
