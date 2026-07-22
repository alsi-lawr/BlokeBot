using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomCommandInvocationSchemaUpgradeTests
{
    [Test]
    public async Task LegacyCustomCommandSchema_Initializing_AddsUnlimitedModeClaimsAndAuditsIdempotently()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using (var setup = await dbFactory.CreateDbContextAsync())
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE hosts (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Login TEXT NOT NULL
                );
                CREATE TABLE custom_commands (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    HostId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    CONSTRAINT AK_custom_commands_HostId_Id UNIQUE (HostId, Id),
                    FOREIGN KEY (HostId) REFERENCES hosts (Id) ON DELETE CASCADE
                );
                CREATE TABLE custom_announcements (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    DeliveryType TEXT NOT NULL DEFAULT 'ChatMessage',
                    AnnouncementColor TEXT NOT NULL DEFAULT 'Primary',
                    LatestDeliveryResult TEXT NOT NULL DEFAULT 'None'
                );
                INSERT INTO hosts (Login) VALUES ('legacy');
                INSERT INTO custom_commands (HostId, Name) VALUES (1, 'Legacy command');
                """
            );
        }

        var initializer = new BlokeBotDatabaseInitializer(dbFactory);
        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var limit = await verify
            .CustomCommands.Select(command => command.InvocationLimit)
            .SingleAsync();
        limit.ShouldBe(CustomCommandInvocationLimit.Unlimited);
        var connection = (SqliteConnection)verify.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN (
                  'custom_command_invocation_claims',
                  'custom_command_invocation_reset_audits'
              );
            """;
        Convert.ToInt32(await command.ExecuteScalarAsync()).ShouldBe(2);
    }
}
