using Microsoft.Data.Sqlite;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private async Task<CutoverReceipt> ApplySelfReferencesAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        SqliteConnection source,
        NpgsqlConnection target,
        CutoverTable table,
        CutoverTableCheckpoint checkpoint,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        var restored = checkpoint.SelfReferenceRowsRestored;
        while (restored < checkpoint.RowsCopied)
        {
            var rows = await ReadSelfReferenceBatchAsync(
                source,
                table,
                restored,
                (int)Math.Min(batchSize, checkpoint.RowsCopied - restored),
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
            restored += rows.Count;
            receipt = receipt.WithCheckpoint(
                new CutoverTableCheckpoint(table.Name, checkpoint.RowsCopied, restored),
                CutoverPhase.RestoringSelfReferences
            );
            await store.WriteAsync(receipt, cancellationToken);
        }

        return receipt;
    }

    private static async Task<IReadOnlyList<CutoverSelfReferenceRow>> ReadSelfReferenceBatchAsync(
        SqliteConnection source,
        CutoverTable table,
        long offset,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var columns = table
            .KeyColumns.Concat(table.SelfReferenceColumns)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await using var command = source.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", columns.Select(CutoverSql.Quote))} FROM {CutoverSql.Quote(table.Name)} ORDER BY {CutoverSql.OrderBy(table, postgreSql: false)} LIMIT $limit OFFSET $offset;";
        _ = command.Parameters.AddWithValue("$limit", limit);
        _ = command.Parameters.AddWithValue("$offset", offset);
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
            $"UPDATE {CutoverSql.Quote(table.Name)} SET {string.Join(", ", table.SelfReferenceColumns.Select((column, index) => $"{CutoverSql.Quote(column)} = @self{index}"))} WHERE {string.Join(" AND ", table.KeyColumns.Select((column, index) => $"{CutoverSql.Quote(column)} = @key{index}"))};";
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

internal sealed record CutoverSelfReferenceRow(string[] Columns, object?[] Values)
{
    internal object? Value(string column) => Values[Array.IndexOf(Columns, column)];

    internal bool HasReference(CutoverTable table) =>
        table.SelfReferenceColumns.Any(column => Value(column) is not null);
}
