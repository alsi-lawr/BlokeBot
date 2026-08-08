using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class AutomationEventReceiptsMigrationTests
{
    private const string _previousMigration =
        "20260804084816_v0.7.0_CustomCommandAutomationRuntime";
    private const string _migration = "20260807121536_v0.7.0_AutomationEventReceipts";

    [Test]
    public async Task Migration_AddsHostIsolatedReceiptTableAndDownRemovesIt()
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
                (1, 'host-id', 'host', 'Host', 0, 65535, 0, '2026-08-07T00:00:00Z');
            """
        );

        await db.GetService<IMigrator>().MigrateAsync(_migration);
        _ = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO automation_event_receipts
                (HostId, SourceDefinitionId, ProviderMessageId, ClaimedAtUtc, ExpiresAtUtc)
            VALUES
                (1, 'cheer', 'message-1', '2026-08-07T12:00:00Z', '2026-08-07T12:10:00Z');

            INSERT OR IGNORE INTO automation_event_receipts
                (HostId, SourceDefinitionId, ProviderMessageId, ClaimedAtUtc, ExpiresAtUtc)
            VALUES
                (1, 'cheer', 'message-1', '2026-08-07T12:01:00Z', '2026-08-07T12:11:00Z');
            """
        );

        (
            await db
                .Database.SqlQueryRaw<string>(
                    "SELECT ClaimedAtUtc AS Value FROM automation_event_receipts"
                )
                .ToArrayAsync()
        ).ShouldBe(["2026-08-07T12:00:00Z"]);

        // Deleting the host cascades its receipts.
        _ = await db.Database.ExecuteSqlRawAsync("DELETE FROM hosts WHERE Id = 1;");
        (
            await db
                .Database.SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS Value FROM automation_event_receipts"
                )
                .SingleAsync()
        ).ShouldBe(0);

        await db.GetService<IMigrator>().MigrateAsync(_previousMigration);
        (
            await db
                .Database.SqlQueryRaw<long>(
                    """
                    SELECT COUNT(*) AS Value
                    FROM sqlite_master
                    WHERE type = 'table' AND name = 'automation_event_receipts'
                    """
                )
                .SingleAsync()
        ).ShouldBe(0);
    }
}
