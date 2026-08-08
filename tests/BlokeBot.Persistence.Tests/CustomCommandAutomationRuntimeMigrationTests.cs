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
    public async Task Downgrade_RecodesAutomationActionsToMessageAcceptedByTheStricterConstraint()
    {
        await using var factory = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        await using var db = await factory.CreateDbContextAsync();
        await db.GetService<IMigrator>().MigrateAsync(_migration);
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
                (1, 1, 'Automation', 1, 1, 0, 0, 'Global', 'Unlimited',
                 '2026-08-04T00:00:00Z', '2026-08-04T00:00:00Z');

            INSERT INTO custom_command_actions (CustomCommandId, HostId, ActionType)
            VALUES (1, 1, 'Automation');
            """
        );

        await db.GetService<IMigrator>().MigrateAsync(_previousMigration);

        (
            await db
                .Database.SqlQueryRaw<string>(
                    "SELECT ActionType AS Value FROM custom_command_actions WHERE CustomCommandId = 1"
                )
                .SingleAsync()
        ).ShouldBe("Message");
        (
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE custom_command_actions SET ActionType = ActionType WHERE CustomCommandId = 1;"
            )
        ).ShouldBe(1);
    }
}
