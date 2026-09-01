using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReceiptResult> ReconcileTargetAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        SqliteConnection source,
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
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
            var checkpoint = receipt.Checkpoints.SingleOrDefault(item =>
                StringComparer.Ordinal.Equals(item.Table, table.Name)
            );
            if (
                targetProjection.Count > sourceProjection.Count
                || (checkpoint is not null && checkpoint.RowsCopied > targetProjection.Count)
            )
            {
                return new(
                    null,
                    $"The PostgreSql target does not match checkpointed table {table.Name}."
                );
            }

            if (checkpoint is not null)
            {
                var checkpointSource = await CutoverProjection.ReadAsync(
                    source,
                    sourceTransaction,
                    table,
                    checkpoint.RowsCopied,
                    cancellationToken
                );
                if (!StringComparer.Ordinal.Equals(checkpoint.PrefixHash, checkpointSource.Hash))
                {
                    return new(null, $"The external checkpoint for table {table.Name} is invalid.");
                }
            }

            if (targetProjection.Count == 0)
            {
                continue;
            }

            var sourcePrefix = await CutoverProjection.ReadAsync(
                source,
                sourceTransaction,
                table,
                targetProjection.Count,
                cancellationToken
            );
            if (!StringComparer.Ordinal.Equals(sourcePrefix.Hash, targetProjection.Hash))
            {
                return new(
                    null,
                    $"The PostgreSql target contains unrelated data in table {table.Name}."
                );
            }

            if (
                checkpoint is null
                || checkpoint.RowsCopied != targetProjection.Count
                || !StringComparer.Ordinal.Equals(checkpoint.PrefixHash, targetProjection.Hash)
            )
            {
                receipt = receipt.WithCheckpoint(
                    new CutoverTableCheckpoint(
                        table.Name,
                        targetProjection.Count,
                        targetProjection.Hash
                    )
                );
                await store.WriteAsync(receipt, cancellationToken);
            }
        }

        return new(receipt, null);
    }

    private static async Task<CutoverReceipt> CopyAsync(
        CutoverReceipt receipt,
        CutoverReceiptStore store,
        SqliteConnection source,
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        foreach (var table in tables)
        {
            var sourceProjection = await CutoverProjection.ReadAsync(
                source,
                sourceTransaction,
                table,
                null,
                cancellationToken
            );
            var copied =
                receipt
                    .Checkpoints.SingleOrDefault(item =>
                        StringComparer.Ordinal.Equals(item.Table, table.Name)
                    )
                    ?.RowsCopied
                ?? 0;
            while (copied < sourceProjection.Count)
            {
                var rows = await ReadSourceBatchAsync(
                    source,
                    sourceTransaction,
                    table,
                    copied,
                    Math.Min(batchSize, sourceProjection.Count - copied),
                    cancellationToken
                );
                await InsertTargetBatchAsync(target, table, rows, cancellationToken);
                copied += rows.Count;
                var sourcePrefix = await CutoverProjection.ReadAsync(
                    source,
                    sourceTransaction,
                    table,
                    copied,
                    cancellationToken
                );
                var targetPrefix = await CutoverProjection.ReadAsync(
                    target,
                    null,
                    table,
                    copied,
                    cancellationToken
                );
                if (
                    sourcePrefix.Count != targetPrefix.Count
                    || !StringComparer.Ordinal.Equals(sourcePrefix.Hash, targetPrefix.Hash)
                )
                {
                    throw new InvalidOperationException(
                        $"PostgreSql changed values while copying table {table.Name}."
                    );
                }

                receipt = receipt.WithCheckpoint(
                    new CutoverTableCheckpoint(table.Name, copied, sourcePrefix.Hash)
                );
                await store.WriteAsync(receipt, cancellationToken);
            }

            if (sourceProjection.Count == 0 && copied == 0)
            {
                receipt = receipt.WithCheckpoint(
                    new CutoverTableCheckpoint(table.Name, 0, sourceProjection.Hash)
                );
                await store.WriteAsync(receipt, cancellationToken);
            }
        }

        return receipt;
    }

    private static async Task<IReadOnlyList<object?[]>> ReadSourceBatchAsync(
        SqliteConnection source,
        SqliteTransaction? transaction,
        CutoverTable table,
        long offset,
        long limit,
        CancellationToken cancellationToken
    )
    {
        await using var command = source.CreateCommand();
        command.Transaction = transaction;
        var columns = string.Join(
            ", ",
            table.Columns.Select(column => CutoverProjection.Quote(column.Name))
        );
        command.CommandText =
            $"SELECT {columns} FROM {CutoverProjection.Quote(table.Name)} ORDER BY {CutoverProjection.OrderBy(table, postgreSql: false)} LIMIT $limit OFFSET $offset;";
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

    private static async Task InsertTargetBatchAsync(
        NpgsqlConnection target,
        CutoverTable table,
        IReadOnlyList<object?[]> rows,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        var columns = string.Join(
            ", ",
            table.Columns.Select(column => CutoverProjection.Quote(column.Name))
        );
        var parameters = string.Join(", ", table.Columns.Select((_, index) => $"@p{index}"));
        foreach (var row in rows)
        {
            await using var command = target.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"INSERT INTO {CutoverProjection.Quote(table.Name)} ({columns}) VALUES ({parameters}) ON CONFLICT DO NOTHING;";
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var column = table.Columns[index];
                _ = command.Parameters.Add(
                    new NpgsqlParameter
                    {
                        ParameterName = $"p{index}",
                        NpgsqlDbType = ParameterType(column.TargetStoreType),
                        Value = row[index] is null
                            ? DBNull.Value
                            : CutoverValues.ForTarget(row[index]!, column.TargetStoreType),
                    }
                );
            }

            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
