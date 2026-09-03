using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReceiptResult> ReconcileTargetAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        IReadOnlyList<CutoverTableRows> sourceRows,
        CancellationToken cancellationToken
    )
    {
        var tableNames = tables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        if (
            receipt.Checkpoints.Select(item => item.Table).Distinct(StringComparer.Ordinal).Count()
                != receipt.Checkpoints.Count
            || receipt.Checkpoints.Any(item => !tableNames.Contains(item.Table))
        )
        {
            return new(null, "The external cutover receipt contains invalid table checkpoints.");
        }

        foreach (var table in tables)
        {
            var sourceCount = SourceCount(sourceRows, table);
            var checkpoint = receipt.Checkpoints.SingleOrDefault(item =>
                StringComparer.Ordinal.Equals(item.Table, table.Name)
            );
            var copied = await CutoverSql.CountAsync(target, table, cancellationToken);
            if (copied > sourceCount)
            {
                return new(
                    null,
                    $"The PostgreSql target contains unrelated data in table {table.Name}."
                );
            }

            if (
                checkpoint is not null
                && (
                    copied < checkpoint.RowsCopied
                    || !ValidSelfReferenceProgress(table, checkpoint, sourceCount)
                )
            )
            {
                return new(
                    null,
                    $"The PostgreSql target does not match checkpointed table {table.Name}."
                );
            }

            if (copied == 0 && checkpoint is null)
            {
                continue;
            }

            // Rows committed without a checkpoint are adopted; self references are restored
            // only from the checkpointed position, and the update is idempotent.
            var reconciled = new CutoverTableCheckpoint(
                table.Name,
                copied,
                table.SelfReferences.Count == 0
                    ? copied
                    : checkpoint?.SelfReferenceRowsRestored ?? 0
            );
            if (reconciled != checkpoint)
            {
                receipt = receipt.WithCheckpoint(reconciled);
                await store.WriteAsync(receipt, cancellationToken);
            }
        }

        return new(receipt, null);
    }

    private static bool ValidSelfReferenceProgress(
        CutoverTable table,
        CutoverTableCheckpoint checkpoint,
        long sourceRows
    ) =>
        checkpoint.RowsCopied >= 0
        && checkpoint.SelfReferenceRowsRestored >= 0
        && checkpoint.SelfReferenceRowsRestored <= checkpoint.RowsCopied
        && (
            table.SelfReferences.Count == 0
                ? checkpoint.SelfReferenceRowsRestored == checkpoint.RowsCopied
                : checkpoint.SelfReferenceRowsRestored == 0 || checkpoint.RowsCopied == sourceRows
        );

    private static long SourceCount(
        IReadOnlyList<CutoverTableRows> sourceRows,
        CutoverTable table
    ) => sourceRows.Single(item => StringComparer.Ordinal.Equals(item.Table, table.Name)).Rows;

    private async Task<CutoverReceipt> CopyAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        SqliteConnection source,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        IReadOnlyList<CutoverTableRows> sourceRows,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        foreach (var table in tables)
        {
            var stageSelfReferences = table.SelfReferences.Count > 0;
            var sourceCount = SourceCount(sourceRows, table);
            var copied =
                receipt
                    .Checkpoints.SingleOrDefault(item =>
                        StringComparer.Ordinal.Equals(item.Table, table.Name)
                    )
                    ?.RowsCopied
                ?? 0;
            while (copied < sourceCount)
            {
                var rows = await ReadSourceBatchAsync(
                    source,
                    table,
                    copied,
                    Math.Min(batchSize, sourceCount - copied),
                    cancellationToken
                );
                if (rows.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"SQLite ended while copying table {table.Name}."
                    );
                }

                await InsertTargetBatchAsync(target, table, rows, cancellationToken);
                copied += rows.Count;
                receipt = receipt.WithCheckpoint(
                    new CutoverTableCheckpoint(
                        table.Name,
                        copied,
                        SelfReferenceRowsRestored: stageSelfReferences ? 0 : copied
                    )
                );
                await store.WriteAsync(receipt, cancellationToken);
            }

            if (sourceCount == 0 && copied == 0)
            {
                receipt = receipt.WithCheckpoint(new CutoverTableCheckpoint(table.Name, 0, 0));
                await store.WriteAsync(receipt, cancellationToken);
            }

            var checkpoint = receipt.Checkpoints.Single(item =>
                StringComparer.Ordinal.Equals(item.Table, table.Name)
            );
            if (stageSelfReferences && checkpoint.SelfReferenceRowsRestored < checkpoint.RowsCopied)
            {
                receipt = await ApplySelfReferencesAsync(
                    receipt,
                    store,
                    source,
                    target,
                    table,
                    checkpoint,
                    batchSize,
                    cancellationToken
                );
            }
        }

        return receipt;
    }

    private static async Task<IReadOnlyList<object?[]>> ReadSourceBatchAsync(
        SqliteConnection source,
        CutoverTable table,
        long offset,
        long limit,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.CreateCommand();
        var columns = string.Join(
            ", ",
            table.Columns.Select(column => CutoverSql.Quote(column.Name))
        );
        command.CommandText =
            $"SELECT {columns} FROM {CutoverSql.Quote(table.Name)} ORDER BY {CutoverSql.OrderBy(table, postgreSql: false)} LIMIT $limit OFFSET $offset;";
        _ = command.Parameters.AddWithValue("$limit", limit);
        _ = command.Parameters.AddWithValue("$offset", offset);
        var rows = new List<object?[]>((int)limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[table.Columns.Count];
            for (var index = 0; index < row.Length; index++)
            {
                row[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task InsertTargetBatchAsync(
        NpgsqlConnection target,
        CutoverTable table,
        IReadOnlyList<object?[]> rows,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        var columns = string.Join(
            ", ",
            table.Columns.Select(column => CutoverSql.Quote(column.Name))
        );
        var parameters = string.Join(", ", table.Columns.Select((_, index) => $"@p{index}"));
        foreach (var row in rows)
        {
            await using var command = target.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"INSERT INTO {CutoverSql.Quote(table.Name)} ({columns}) VALUES ({parameters}) ON CONFLICT DO NOTHING;";
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var column = table.Columns[index];
                _ = command.Parameters.Add(
                    new NpgsqlParameter
                    {
                        ParameterName = $"p{index}",
                        NpgsqlDbType = ParameterType(column.TargetStoreType),
                        Value =
                            row[index] is null || table.SelfReferenceColumns.Contains(column.Name)
                                ? DBNull.Value
                                : CutoverValues.ForTarget(row[index]!, column.TargetStoreType),
                    }
                );
            }

            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _batchCommitted?.Invoke(
            new CutoverBatchCommit(table.Name, rows.Count, CutoverBatchPhase.Copy)
        );
    }

    private static NpgsqlDbType ParameterType(string storeType) =>
        storeType switch
        {
            "boolean" => NpgsqlDbType.Boolean,
            "integer" => NpgsqlDbType.Integer,
            "bigint" => NpgsqlDbType.Bigint,
            "numeric" => NpgsqlDbType.Numeric,
            "uuid" => NpgsqlDbType.Uuid,
            "timestamp with time zone" => NpgsqlDbType.TimestampTz,
            "time without time zone" => NpgsqlDbType.Time,
            "bytea" => NpgsqlDbType.Bytea,
            _ => NpgsqlDbType.Text,
        };
}

internal enum CutoverBatchPhase
{
    Copy,
    SelfReferenceRestoration,
}

internal sealed record CutoverBatchCommit(string Table, int Rows, CutoverBatchPhase Phase);
