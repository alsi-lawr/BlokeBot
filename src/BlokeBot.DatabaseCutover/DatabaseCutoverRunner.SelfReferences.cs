using Microsoft.Data.Sqlite;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReconciledTable> ReconcileTableAsync(
        SqliteConnection source,
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        CutoverTable table,
        TableProjection sourceProjection,
        CutoverTableCheckpoint? checkpoint,
        CancellationToken cancellationToken
    )
    {
        var targetFinal = await CutoverProjection.ReadAsync(
            target,
            null,
            table,
            null,
            cancellationToken
        );
        if (
            targetFinal.Count > sourceProjection.Count
            || (checkpoint is not null && checkpoint.RowsCopied > targetFinal.Count)
            || (
                checkpoint is { SelfReferencesApplied: true }
                && checkpoint.RowsCopied < sourceProjection.Count
            )
        )
        {
            return CutoverReconciledTable.Rejected(
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
                cancellationToken,
                stageSelfReferences: !checkpoint.SelfReferencesApplied
            );
            if (!StringComparer.Ordinal.Equals(checkpoint.PrefixHash, checkpointSource.Hash))
            {
                return CutoverReconciledTable.Rejected(
                    $"The external checkpoint for table {table.Name} is invalid."
                );
            }
        }

        if (targetFinal.Count == 0)
        {
            var emptyIsFinal = sourceProjection.Count == 0 || table.SelfReferences.Count == 0;
            return new(targetFinal, emptyIsFinal, null);
        }

        var sourcePrefix = await CutoverProjection.ReadAsync(
            source,
            sourceTransaction,
            table,
            targetFinal.Count,
            cancellationToken
        );
        if (StringComparer.Ordinal.Equals(sourcePrefix.Hash, targetFinal.Hash))
        {
            return new(targetFinal, SelfReferencesApplied: true, null);
        }

        if (
            table.SelfReferences.Count == 0
            || await HasSelfReferenceValuesAsync(target, table, cancellationToken)
        )
        {
            return CutoverReconciledTable.Rejected(
                $"The PostgreSql target contains unrelated data in table {table.Name}."
            );
        }

        var sourceStaged = await CutoverProjection.ReadAsync(
            source,
            sourceTransaction,
            table,
            targetFinal.Count,
            cancellationToken,
            stageSelfReferences: true
        );
        var targetStaged = await CutoverProjection.ReadAsync(
            target,
            null,
            table,
            targetFinal.Count,
            cancellationToken,
            stageSelfReferences: true
        );
        return StringComparer.Ordinal.Equals(sourceStaged.Hash, targetStaged.Hash)
            ? new(targetStaged, SelfReferencesApplied: false, null)
            : CutoverReconciledTable.Rejected(
                $"The PostgreSql target contains unrelated data in table {table.Name}."
            );
    }

    private static async Task<bool> HasSelfReferenceValuesAsync(
        NpgsqlConnection target,
        CutoverTable table,
        CancellationToken cancellationToken
    )
    {
        await using var command = target.CreateCommand();
        command.CommandText =
            $"SELECT EXISTS (SELECT 1 FROM {CutoverProjection.Quote(table.Name)} WHERE {string.Join(" OR ", table.SelfReferenceColumns.Select(column => $"{CutoverProjection.Quote(column)} IS NOT NULL"))});";
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ApplySelfReferencesAsync(
        SqliteConnection source,
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        CutoverTable table,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        long offset = 0;
        while (true)
        {
            var rows = await ReadSelfReferenceBatchAsync(
                source,
                sourceTransaction,
                table,
                offset,
                batchSize,
                cancellationToken
            );
            if (rows.Count == 0)
            {
                break;
            }

            foreach (var row in rows.Where(row => row.HasReference))
            {
                await UpdateSelfReferencesAsync(target, transaction, table, row, cancellationToken);
            }

            offset += rows.Count;
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<CutoverSelfReferenceRow>> ReadSelfReferenceBatchAsync(
        SqliteConnection source,
        SqliteTransaction? transaction,
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
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {string.Join(", ", columns.Select(CutoverProjection.Quote))} FROM {CutoverProjection.Quote(table.Name)} ORDER BY {CutoverProjection.OrderBy(table, postgreSql: false)} LIMIT $limit OFFSET $offset;";
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
                        .ToArray(),
                    table.SelfReferenceColumns.Any(column =>
                        !reader.IsDBNull(reader.GetOrdinal(column))
                    )
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

internal sealed record CutoverReconciledTable(
    TableProjection? Projection,
    bool SelfReferencesApplied,
    string? Failure
)
{
    internal static CutoverReconciledTable Rejected(string failure) =>
        new(null, SelfReferencesApplied: false, failure);
}

internal sealed record CutoverSelfReferenceRow(
    string[] Columns,
    object?[] Values,
    bool HasReference
)
{
    internal object? Value(string column) => Values[Array.IndexOf(Columns, column)];
}
