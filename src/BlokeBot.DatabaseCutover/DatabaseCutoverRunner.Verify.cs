using System.Globalization;
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
                var sequence = await SequenceNameAsync(
                    target,
                    transaction,
                    table,
                    identity,
                    cancellationToken
                );
                if (sequence is null)
                {
                    continue;
                }

                var maximum = await MaximumIdentityAsync(
                    target,
                    transaction,
                    table,
                    identity,
                    cancellationToken
                );
                await using var set = target.CreateCommand();
                set.Transaction = transaction;
                set.CommandText = "SELECT setval(CAST(@sequence AS regclass), @value, @is_called);";
                _ = set.Parameters.AddWithValue("sequence", sequence);
                _ = set.Parameters.AddWithValue("value", maximum is > 0 ? maximum.Value : 1L);
                _ = set.Parameters.AddWithValue("is_called", maximum is > 0);
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
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        string sourceFingerprint,
        string localStateFingerprint,
        DatabaseCutoverOptions options,
        IReadOnlyList<string> sourceMigrations,
        CancellationToken cancellationToken
    )
    {
        receipt = receipt.WithPhase(CutoverPhase.Verifying);
        await store.WriteAsync(receipt, cancellationToken);

        var currentSourceFingerprint = await CutoverFingerprint.SourceAsync(
            source,
            sourceTransaction,
            sourceMigrations,
            tables,
            cancellationToken
        );
        if (!StringComparer.Ordinal.Equals(sourceFingerprint, currentSourceFingerprint))
        {
            return new(null, "The SQLite source changed during cutover verification.");
        }

        var targetProjections = new List<(string Table, TableProjection Projection)>(tables.Count);
        foreach (var table in tables)
        {
            var sourceProjection = await CutoverProjection.ReadAsync(
                source,
                sourceTransaction,
                table,
                null,
                cancellationToken
            );
            var targetProjection = await CutoverProjection.ReadAsync(
                target,
                null,
                table,
                null,
                cancellationToken
            );
            if (
                sourceProjection.Count != targetProjection.Count
                || !StringComparer.Ordinal.Equals(sourceProjection.Hash, targetProjection.Hash)
            )
            {
                return new(null, $"Verification failed for domain table {table.Name}.");
            }

            targetProjections.Add((table.Name, targetProjection));
        }

        var constraintFailure = await ValidateTargetConstraintsAsync(target, cancellationToken);
        if (constraintFailure is not null)
        {
            return new(null, constraintFailure);
        }

        var sequenceFailure = await ValidateSequencesAsync(target, tables, cancellationToken);
        if (sequenceFailure is not null)
        {
            return new(null, sequenceFailure);
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

        receipt = receipt.Verified(CutoverFingerprint.Verification(targetProjections));
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

    private static async Task<string?> ValidateSequencesAsync(
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        foreach (var table in tables)
        {
            foreach (var identity in table.Identities)
            {
                var sequence = await SequenceNameAsync(
                    target,
                    transaction,
                    table,
                    identity,
                    cancellationToken
                );
                var maximum = await MaximumIdentityAsync(
                    target,
                    transaction,
                    table,
                    identity,
                    cancellationToken
                );
                if (sequence is null || maximum is null)
                {
                    continue;
                }

                await using var command = target.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT pg_sequence_last_value(CAST(@sequence AS regclass));";
                _ = command.Parameters.AddWithValue("sequence", sequence);
                var lastValue = await command.ExecuteScalarAsync(cancellationToken);
                if (
                    maximum > 0
                    && (
                        lastValue is null or DBNull
                        || Convert.ToInt64(lastValue, CultureInfo.InvariantCulture) < maximum
                    )
                )
                {
                    return $"The PostgreSql identity sequence for {table.Name} was not advanced.";
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private static async Task<string?> SequenceNameAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        CutoverTable table,
        CutoverIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_get_serial_sequence(@table_name, @column_name);";
        _ = command.Parameters.AddWithValue("table_name", $"public.{table.Name}");
        _ = command.Parameters.AddWithValue("column_name", identity.Column);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<long?> MaximumIdentityAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        CutoverTable table,
        CutoverIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT MAX({CutoverProjection.Quote(identity.Column)})::bigint FROM {CutoverProjection.Quote(table.Name)};";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}
