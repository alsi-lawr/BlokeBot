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
        int batchSize,
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
            || !ValidSelfReferenceProgress(table, checkpoint, sourceProjection.Count)
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
                stageSelfReferences: table.SelfReferences.Count > 0
                    && checkpoint.SelfReferenceRowsRestored < checkpoint.RowsCopied
            );
            if (!StringComparer.Ordinal.Equals(checkpoint.PrefixHash, checkpointSource.Hash))
            {
                return CutoverReconciledTable.Rejected(
                    $"The external checkpoint for table {table.Name} is invalid."
                );
            }
        }

        var restored = checkpoint?.SelfReferenceRowsRestored ?? 0;
        if (table.SelfReferences.Count == 0)
        {
            var sourcePrefix = await CutoverProjection.ReadAsync(
                source,
                sourceTransaction,
                table,
                targetFinal.Count,
                cancellationToken
            );
            return StringComparer.Ordinal.Equals(sourcePrefix.Hash, targetFinal.Hash)
                ? new(targetFinal, targetFinal.Count, null)
                : CutoverReconciledTable.Rejected(
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
        if (
            !StringComparer.Ordinal.Equals(sourceStaged.Hash, targetStaged.Hash)
            || !await SelfReferenceStateIsRecoverableAsync(
                source,
                sourceTransaction,
                target,
                table,
                targetFinal.Count,
                restored,
                batchSize,
                cancellationToken
            )
        )
        {
            return CutoverReconciledTable.Rejected(
                $"The PostgreSql target contains unrelated data in table {table.Name}."
            );
        }

        if (restored < targetFinal.Count)
        {
            return new(targetStaged, restored, null);
        }

        var sourceFinal = await CutoverProjection.ReadAsync(
            source,
            sourceTransaction,
            table,
            targetFinal.Count,
            cancellationToken
        );
        return StringComparer.Ordinal.Equals(sourceFinal.Hash, targetFinal.Hash)
            ? new(targetFinal, restored, null)
            : CutoverReconciledTable.Rejected(
                $"The PostgreSql target contains unrelated data in table {table.Name}."
            );
    }

    private static bool ValidSelfReferenceProgress(
        CutoverTable table,
        CutoverTableCheckpoint? checkpoint,
        long sourceRows
    ) =>
        checkpoint is null
        || (
            checkpoint.RowsCopied >= 0
            && checkpoint.SelfReferenceRowsRestored >= 0
            && checkpoint.SelfReferenceRowsRestored <= checkpoint.RowsCopied
            && (
                table.SelfReferences.Count == 0
                    ? checkpoint.SelfReferenceRowsRestored == checkpoint.RowsCopied
                    : checkpoint.SelfReferenceRowsRestored == 0
                        || checkpoint.RowsCopied == sourceRows
            )
        );

    private static async Task<bool> SelfReferenceStateIsRecoverableAsync(
        SqliteConnection source,
        SqliteTransaction? sourceTransaction,
        NpgsqlConnection target,
        CutoverTable table,
        long rowCount,
        long restored,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        long offset = 0;
        while (offset < rowCount)
        {
            var limit = (int)Math.Min(batchSize, rowCount - offset);
            var sourceRows = await ReadSelfReferenceBatchAsync(
                source,
                sourceTransaction,
                table,
                offset,
                limit,
                postgreSql: false,
                cancellationToken
            );
            var targetRows = await ReadSelfReferenceBatchAsync(
                target,
                null,
                table,
                offset,
                limit,
                postgreSql: true,
                cancellationToken
            );
            if (sourceRows.Count == 0 || sourceRows.Count != targetRows.Count)
            {
                return false;
            }

            for (var index = 0; index < sourceRows.Count; index++)
            {
                var matchesSource = SelfReferencesMatch(
                    table,
                    sourceRows[index],
                    targetRows[index]
                );
                if (
                    (offset + index < restored && !matchesSource)
                    || (
                        offset + index >= restored
                        && !matchesSource
                        && !targetRows[index].SelfReferencesAreNull(table)
                    )
                )
                {
                    return false;
                }
            }

            offset += sourceRows.Count;
        }

        return true;
    }
}

internal sealed record CutoverReconciledTable(
    TableProjection? Projection,
    long SelfReferenceRowsRestored,
    string? Failure
)
{
    internal static CutoverReconciledTable Rejected(string failure) => new(null, 0, failure);
}

internal sealed record CutoverSelfReferenceRow(string[] Columns, object?[] Values)
{
    internal object? Value(string column) => Values[Array.IndexOf(Columns, column)];

    internal bool HasReference(CutoverTable table) =>
        table.SelfReferenceColumns.Any(column => Value(column) is not null);

    internal bool SelfReferencesAreNull(CutoverTable table) =>
        table.SelfReferenceColumns.All(column => Value(column) is null);
}
