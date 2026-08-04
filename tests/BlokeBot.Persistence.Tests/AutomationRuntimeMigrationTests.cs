using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Persistence.Tests;

public sealed class AutomationRuntimeMigrationTests
{
    private const string _previousMigration = "20260802075446_v0.6.0_ViewerQueueOverlay";
    private const string _migration = "20260803232049_v0.7.0_AutomationRuntime";

    [Test]
    public async Task Upgrade_AddsHostScopedRuntimeSchemaWithoutChangingExistingFeatures()
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
                    (1, 'host-id', 'host', 'Host', 0, 8191, 0, '2026-08-03T00:00:00Z');
                """
            );
            await before.Database.MigrateAsync();
        }

        await using var upgraded = await factory.CreateDbContextAsync();
        (await upgraded.Database.GetAppliedMigrationsAsync()).ShouldContain(_migration);
        (await upgraded.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (
            await ScalarAsync(
                upgraded.Database.GetDbConnection(),
                "SELECT EnabledFeatures FROM hosts WHERE Id = 1;"
            )
        ).ShouldBe(8191);
        (
            await ScalarAsync(
                upgraded.Database.GetDbConnection(),
                "SELECT AutomationGeneration FROM hosts WHERE Id = 1;"
            )
        ).ShouldBe(0);
        (
            await ScalarAsync(
                upgraded.Database.GetDbConnection(),
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'automation_flows',
                    'automation_flow_nodes',
                    'automation_flow_edges',
                    'automation_flow_runs',
                    'automation_node_runs'
                  );
                """
            )
        ).ShouldBe(5);
        (
            await ScalarAsync(
                upgraded.Database.GetDbConnection(),
                """
                SELECT COUNT(*) FROM pragma_table_info('automation_flow_runs')
                WHERE name IN ('ContextSchemaVersion', 'ExecutionLeaseId');
                """
            )
        ).ShouldBe(2);

        await upgraded.GetService<IMigrator>().MigrateAsync(_previousMigration);
        (
            await ScalarAsync(
                upgraded.Database.GetDbConnection(),
                "SELECT COUNT(*) FROM hosts WHERE Id = 1;"
            )
        ).ShouldBe(1);
        (
            await ScalarAsync(
                upgraded.Database.GetDbConnection(),
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table' AND name LIKE 'automation_%';
                """
            )
        ).ShouldBe(0);
    }

    private static async Task<long> ScalarAsync(DbConnection connection, string sql)
    {
        if (connection.State is not System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
