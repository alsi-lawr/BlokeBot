using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class CustomCommandAutomationRuntimeMigrationTests
{
    private const string _previousMigration =
        "20260804000549_v0.7.0_CustomCommandSelectedUserAccess";
    private const string _migration = "20260804084816_v0.7.0_CustomCommandAutomationRuntime";

    [Test]
    public async Task Migration_AddsBoundedAutomationActionAndDowngradesItToMessage()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.GetService<IMigrator>().MigrateAsync(_previousMigration);
        _ = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO hosts
                (Id, TwitchUserId, Login, DisplayName, BotRuntimeState, EnabledFeatures,
                 CommandsAliasesConfigured, CreatedAtUtc)
            VALUES
                (1, 'host-id', 'host', 'Host', 0, 65535, 0, '2026-08-04T00:00:00Z');

            INSERT INTO custom_commands
                (Id, HostId, Name, Enabled, AllowEveryone, AllowModerators, CooldownSeconds,
                 CooldownScope, InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (1, 1, 'Message', 1, 1, 0, 0, 'Global', 'Unlimited',
                 '2026-08-04T00:00:00Z', '2026-08-04T00:00:00Z');

            INSERT INTO custom_command_actions (CustomCommandId, HostId, ActionType)
            VALUES (1, 1, 'Message');
            """
        );

        await db.GetService<IMigrator>().MigrateAsync(_migration);
        _ = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO custom_commands
                (Id, HostId, Name, Enabled, AllowEveryone, AllowModerators, CooldownSeconds,
                 CooldownScope, InvocationLimit, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (2, 1, 'Automation', 1, 1, 0, 0, 'Global', 'Unlimited',
                 '2026-08-04T00:00:00Z', '2026-08-04T00:00:00Z'),
                (3, 1, 'Invalid automation', 1, 1, 0, 0, 'Global', 'Unlimited',
                 '2026-08-04T00:00:00Z', '2026-08-04T00:00:00Z');

            INSERT INTO custom_command_actions (CustomCommandId, HostId, ActionType)
            VALUES (2, 1, 'Automation');
            """
        );

        _ = (
            await db
                .CustomCommands.AsNoTracking()
                .Include(command => command.Action)
                .SingleAsync(command => command.Id == 2)
        ).Action.ShouldBeOfType<AutomationCustomCommandAction>();
        _ = await Should.ThrowAsync<SqliteException>(() =>
            db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO custom_command_actions
                    (CustomCommandId, HostId, ActionType, QueuePolicy)
                VALUES (3, 1, 'Automation', 'enqueue');
                """
            )
        );

        await db.GetService<IMigrator>().MigrateAsync(_previousMigration);
        (
            await db
                .Database.SqlQueryRaw<string>(
                    """
                    SELECT ActionType AS Value
                    FROM custom_command_actions
                    ORDER BY CustomCommandId
                    """
                )
                .ToArrayAsync()
        ).ShouldBe(["Message", "Message"]);
    }
}
