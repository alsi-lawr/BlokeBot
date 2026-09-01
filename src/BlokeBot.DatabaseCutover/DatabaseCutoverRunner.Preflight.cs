using System.Globalization;
using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<string?> AcquireTargetOwnershipAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var lockCommand = connection.CreateCommand();
        lockCommand.CommandText = "SELECT pg_try_advisory_lock(@lock_key);";
        _ = lockCommand.Parameters.AddWithValue(
            "lock_key",
            BlokeBotDatabaseRuntimeLease.OwnershipLockKey
        );
        if (!(bool)(await lockCommand.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            return "The PostgreSql target is in use by BlokeBot or another cutover operation.";
        }

        await using var sessions = connection.CreateCommand();
        sessions.CommandText =
            "SELECT COUNT(*) FROM pg_stat_activity WHERE datid = (SELECT oid FROM pg_database WHERE datname = current_database()) AND pid <> pg_backend_pid();";
        var otherSessions = Convert.ToInt64(
            await sessions.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
        if (otherSessions == 0)
        {
            return null;
        }

        await ReleaseTargetOwnershipAsync(connection);
        return "The PostgreSql target has another active session. Stop all target users before cutover.";
    }

    private static async Task ReleaseTargetOwnershipAsync(NpgsqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@lock_key);";
        _ = command.Parameters.AddWithValue(
            "lock_key",
            BlokeBotDatabaseRuntimeLease.OwnershipLockKey
        );
        _ = await command.ExecuteScalarAsync();
    }

    private static async Task<CutoverSchemaValidation> ValidateSchemaAsync(
        BlokeBotDbContext source,
        BlokeBotDbContext target,
        CancellationToken cancellationToken
    )
    {
        var sourceAvailable = source.Database.GetMigrations().ToArray();
        var sourceApplied = (
            await source.Database.GetAppliedMigrationsAsync(cancellationToken)
        ).ToArray();
        if (
            sourceAvailable.LastOrDefault() != _currentSqliteMigration
            || !sourceAvailable.SequenceEqual(sourceApplied, StringComparer.Ordinal)
        )
        {
            return new(
                sourceApplied,
                [],
                "The SQLite source is not the supported released v0.13 schema. Start v0.13 first to finish its forward migrations."
            );
        }

        var targetAvailable = target.Database.GetMigrations().ToArray();
        var targetApplied = (
            await target.Database.GetAppliedMigrationsAsync(cancellationToken)
        ).ToArray();
        return
            targetAvailable.LastOrDefault() != _currentPostgreSqlMigration
            || !targetAvailable.SequenceEqual(targetApplied, StringComparer.Ordinal)
            ? new(
                sourceApplied,
                targetApplied,
                "The PostgreSql target is not at the compatible v0.14 schema. Apply its forward migrations before cutover."
            )
            : new(sourceApplied, targetApplied, null);
    }

    private static async Task<string?> ValidatePhysicalCatalogsAsync(
        SqliteConnection source,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        var expected = tables.Select(table => table.Name).Order(StringComparer.Ordinal).ToArray();
        await using var sourceCommand = source.CreateCommand();
        sourceCommand.CommandText =
            "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY name;";
        var sourceTables = await ReadStringsAsync(sourceCommand, cancellationToken);
        if (!expected.SequenceEqual(sourceTables, StringComparer.Ordinal))
        {
            return "The SQLite physical domain table catalog does not match the reviewed cutover catalog.";
        }

        await using var targetCommand = target.CreateCommand();
        targetCommand.CommandText =
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY tablename;";
        var targetTables = await ReadStringsAsync(targetCommand, cancellationToken);
        return expected.SequenceEqual(targetTables, StringComparer.Ordinal)
            ? null
            : "The PostgreSql physical domain table catalog does not match the reviewed cutover catalog.";
    }

    private static async Task<string[]> ReadStringsAsync(
        System.Data.Common.DbCommand command,
        CancellationToken cancellationToken
    )
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }
}
