using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private async Task<CutoverReceipt> ApplySelfReferencesAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        SqliteConnection source,
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        CutoverTable table,
        CutoverTableCheckpoint checkpoint,
        TableProjection sourceFinal,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        var sourceStaged = await CutoverProjection.ReadAsync(
            source,
            sourceTransaction,
            table,
            null,
            cancellationToken,
            stageSelfReferences: true
        );
        var restored = checkpoint.SelfReferenceRowsRestored;
        while (restored < checkpoint.RowsCopied)
        {
            var rows = await ReadSelfReferenceBatchAsync(
                source,
                sourceTransaction,
                table,
                restored,
                (int)Math.Min(batchSize, checkpoint.RowsCopied - restored),
                postgreSql: false,
                cancellationToken
            );
            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"SQLite ended while restoring self references in table {table.Name}."
                );
            }

            await using (var transaction = await target.BeginTransactionAsync(cancellationToken))
            {
                foreach (var row in rows.Where(row => row.HasReference(table)))
                {
                    await UpdateSelfReferencesAsync(
                        target,
                        transaction,
                        table,
                        row,
                        cancellationToken
                    );
                }

                await transaction.CommitAsync(cancellationToken);
            }

            _batchCommitted?.Invoke(
                new CutoverBatchCommit(
                    table.Name,
                    rows.Count,
                    CutoverBatchPhase.SelfReferenceRestoration
                )
            );
            await VerifyRestoredBatchAsync(target, table, restored, rows, cancellationToken);
            restored += rows.Count;

            var prefix = sourceStaged;
            if (restored == checkpoint.RowsCopied)
            {
                var targetFinal = await CutoverProjection.ReadAsync(
                    target,
                    null,
                    table,
                    null,
                    cancellationToken
                );
                if (!StringComparer.Ordinal.Equals(sourceFinal.Hash, targetFinal.Hash))
                {
                    throw new InvalidOperationException(
                        $"PostgreSql changed self references while copying table {table.Name}."
                    );
                }

                prefix = sourceFinal;
            }

            receipt = receipt.WithCheckpoint(
                new CutoverTableCheckpoint(
                    table.Name,
                    checkpoint.RowsCopied,
                    prefix.Hash,
                    restored
                ),
                CutoverPhase.RestoringSelfReferences
            );
            await store.WriteAsync(receipt, cancellationToken);
        }

        return receipt;
    }

    private static async Task VerifyRestoredBatchAsync(
        NpgsqlConnection target,
        CutoverTable table,
        long offset,
        IReadOnlyList<CutoverSelfReferenceRow> sourceRows,
        CancellationToken cancellationToken
    )
    {
        var targetRows = await ReadSelfReferenceBatchAsync(
            target,
            null,
            table,
            offset,
            sourceRows.Count,
            postgreSql: true,
            cancellationToken
        );
        if (
            sourceRows.Count != targetRows.Count
            || sourceRows
                .Where((row, index) => !SelfReferencesMatch(table, row, targetRows[index]))
                .Any()
        )
        {
            throw new InvalidOperationException(
                $"PostgreSql changed one self-reference batch in table {table.Name}."
            );
        }
    }

    private static async Task<IReadOnlyList<CutoverSelfReferenceRow>> ReadSelfReferenceBatchAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CutoverTable table,
        long offset,
        int limit,
        bool postgreSql,
        CancellationToken cancellationToken
    )
    {
        var columns = table
            .KeyColumns.Concat(table.SelfReferenceColumns)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {string.Join(", ", columns.Select(CutoverProjection.Quote))} FROM {CutoverProjection.Quote(table.Name)} ORDER BY {CutoverProjection.OrderBy(table, postgreSql)} LIMIT @limit OFFSET @offset;";
        AddParameter(command, "limit", limit);
        AddParameter(command, "offset", offset);
        var rows = new List<CutoverSelfReferenceRow>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new CutoverSelfReferenceRow(
                    columns,
                    columns
                        .Select(column =>
                            reader.IsDBNull(reader.GetOrdinal(column))
                                ? null
                                : reader.GetValue(reader.GetOrdinal(column))
                        )
                        .ToArray()
                )
            );
        }

        return rows;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        _ = command.Parameters.Add(parameter);
    }

    private static bool SelfReferencesMatch(
        CutoverTable table,
        CutoverSelfReferenceRow source,
        CutoverSelfReferenceRow target
    ) =>
        StringComparer.Ordinal.Equals(
            SelfReferenceHash(table, source),
            SelfReferenceHash(table, target)
        );

    private static string SelfReferenceHash(CutoverTable table, CutoverSelfReferenceRow row)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var column in table.SelfReferenceColumns.Order(StringComparer.Ordinal))
        {
            var definition = table.Columns.Single(candidate => candidate.Name == column);
            CutoverValues.AppendCanonical(hash, row.Value(column), definition.TargetStoreType);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task UpdateSelfReferencesAsync(
        NpgsqlConnection target,
        NpgsqlTransaction transaction,
        CutoverTable table,
        CutoverSelfReferenceRow row,
        CancellationToken cancellationToken
    )
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"UPDATE {CutoverProjection.Quote(table.Name)} SET {string.Join(", ", table.SelfReferenceColumns.Select((column, index) => $"{CutoverProjection.Quote(column)} = @self{index}"))} WHERE {string.Join(" AND ", table.KeyColumns.Select((column, index) => $"{CutoverProjection.Quote(column)} = @key{index}"))};";
        AddValues(command, "self", table, table.SelfReferenceColumns, row);
        AddValues(command, "key", table, table.KeyColumns, row);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                $"PostgreSql could not restore one self reference in table {table.Name}."
            );
        }
    }

    private static void AddValues(
        NpgsqlCommand command,
        string prefix,
        CutoverTable table,
        IEnumerable<string> columns,
        CutoverSelfReferenceRow row
    )
    {
        foreach (var (column, index) in columns.Select((column, index) => (column, index)))
        {
            var definition = table.Columns.Single(candidate => candidate.Name == column);
            _ = command.Parameters.Add(
                new NpgsqlParameter
                {
                    ParameterName = $"{prefix}{index}",
                    NpgsqlDbType = ParameterType(definition.TargetStoreType),
                    Value = row.Value(column) is { } value
                        ? CutoverValues.ForTarget(value, definition.TargetStoreType)
                        : DBNull.Value,
                }
            );
        }
    }
}
