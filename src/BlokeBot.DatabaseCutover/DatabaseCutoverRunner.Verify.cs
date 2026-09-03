using System.Globalization;
using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReceipt> AdvanceSequencesAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        receipt = receipt.WithPhase(CutoverPhase.AdvancingSequences);
        await store.WriteAsync(receipt, cancellationToken);
        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        foreach (var table in tables)
        {
            foreach (var identity in table.Identities)
            {
                // setval is strict, so a column without a sequence yields NULL and advances nothing.
                await using var set = target.CreateCommand();
                set.Transaction = transaction;
                set.CommandText =
                    $"SELECT setval(pg_get_serial_sequence(@table_name, @column_name)::regclass, COALESCE(MAX({CutoverSql.Quote(identity)})::bigint, 1), MAX({CutoverSql.Quote(identity)}) IS NOT NULL) FROM {CutoverSql.Quote(table.Name)};";
                _ = set.Parameters.AddWithValue("table_name", $"public.{table.Name}");
                _ = set.Parameters.AddWithValue("column_name", identity);
                _ = await set.ExecuteScalarAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return receipt;
    }

    private static async Task<CutoverReceiptResult> VerifyAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        SqliteConnection source,
        BlokeBotDbContext targetContext,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        string localStateFingerprint,
        DatabaseCutoverOptions options,
        CancellationToken cancellationToken
    )
    {
        receipt = receipt.WithPhase(CutoverPhase.Verifying);
        await store.WriteAsync(receipt, cancellationToken);

        foreach (var table in tables)
        {
            var sourceRows = await CutoverSql.CountAsync(source, table, cancellationToken);
            var targetRows = await CutoverSql.CountAsync(target, table, cancellationToken);
            if (sourceRows != targetRows)
            {
                return new(
                    null,
                    $"Verification failed for domain table {table.Name}: SQLite has {sourceRows} rows and PostgreSql has {targetRows}."
                );
            }
        }

        var migrations = await ReadMigrationHistoryAsync(
            targetContext,
            _currentPostgreSqlMigration,
            cancellationToken
        );
        if (!migrations.IsCurrent)
        {
            return new(null, "The PostgreSql migration history is not at the current baseline.");
        }

        var catalogFailure = await ValidatePostgreSqlPhysicalCatalogAsync(
            target,
            tables,
            cancellationToken
        );
        if (catalogFailure is not null)
        {
            return new(null, catalogFailure);
        }

        var constraintFailure = await ValidateTargetConstraintsAsync(target, cancellationToken);
        if (constraintFailure is not null)
        {
            return new(null, constraintFailure);
        }

        var currentLocalStateFingerprint = await LocalStateFingerprint.CalculateAsync(
            options.StateDirectory,
            options.SqliteDatabasePath,
            store,
            cancellationToken
        );
        if (!StringComparer.Ordinal.Equals(localStateFingerprint, currentLocalStateFingerprint))
        {
            return new(null, "A provider-neutral local state asset changed during cutover.");
        }

        receipt = receipt.WithPhase(CutoverPhase.Verified);
        await store.WriteAsync(receipt, cancellationToken);
        return new(receipt, null);
    }

    private static async Task<string?> ValidateTargetConstraintsAsync(
        NpgsqlConnection target,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        await using (var immediate = target.CreateCommand())
        {
            immediate.Transaction = transaction;
            immediate.CommandText = "SET CONSTRAINTS ALL IMMEDIATE;";
            _ = await immediate.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM pg_constraint WHERE connamespace = 'public'::regnamespace AND contype = 'f' AND NOT convalidated;";
        var unvalidated = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
        await transaction.CommitAsync(cancellationToken);
        return unvalidated == 0
            ? null
            : "The PostgreSql target contains an unvalidated foreign-key constraint.";
    }
}
