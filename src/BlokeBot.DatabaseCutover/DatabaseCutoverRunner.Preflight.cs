using System.Data.Common;
using System.Globalization;
using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private const long _cutoverOwnershipLockKey = 0x424C4F4B45424F54;

    private static async Task<string?> AcquireTargetOwnershipAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var lockCommand = connection.CreateCommand();
        lockCommand.CommandText = "SELECT pg_try_advisory_lock(@lock_key);";
        _ = lockCommand.Parameters.AddWithValue("lock_key", _cutoverOwnershipLockKey);
        if (!(bool)(await lockCommand.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            return "The PostgreSql target is in use by another cutover operation.";
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
        _ = command.Parameters.AddWithValue("lock_key", _cutoverOwnershipLockKey);
        _ = await command.ExecuteScalarAsync();
    }

    private static async Task<CutoverMigrationHistory> ReadMigrationHistoryAsync(
        BlokeBotDbContext db,
        string currentMigration,
        CancellationToken cancellationToken
    )
    {
        var available = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        return new(
            applied,
            available.LastOrDefault() == currentMigration
                && available.SequenceEqual(applied, StringComparer.Ordinal)
        );
    }

    private static async Task<string?> ValidateSqlitePhysicalCatalogAsync(
        SqliteConnection source,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY name;";
        return await MatchesCatalogAsync(command, tables, cancellationToken)
            ? null
            : "The SQLite physical domain table catalog does not match the reviewed cutover catalog.";
    }

    private static async Task<string?> ValidatePostgreSqlPhysicalCatalogAsync(
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        await using var command = target.CreateCommand();
        command.CommandText =
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY tablename;";
        return await MatchesCatalogAsync(command, tables, cancellationToken)
            ? null
            : "The PostgreSql physical domain table catalog does not match the reviewed cutover catalog.";
    }

    private static async Task<bool> MatchesCatalogAsync(
        DbCommand command,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        var expected = tables.Select(table => table.Name).Order(StringComparer.Ordinal);
        var actual = await ReadStringsAsync(command, cancellationToken);
        return expected.SequenceEqual(actual, StringComparer.Ordinal);
    }

    private static async Task<string[]> ReadStringsAsync(
        DbCommand command,
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
