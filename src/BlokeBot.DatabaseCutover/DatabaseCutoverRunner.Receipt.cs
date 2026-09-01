using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReceiptResult> BindReceiptAsync(
        CutoverReceiptStore store,
        CutoverReceipt? existing,
        Guid? requestedOperationId,
        string sourceFingerprint,
        string targetFingerprint,
        string localStateFingerprint,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        if (existing is not null)
        {
            if (requestedOperationId is { } requested && requested != existing.OperationId)
            {
                return new(
                    null,
                    "The requested operation ID does not match the external cutover receipt."
                );
            }

            var fingerprintsMatch =
                StringComparer.Ordinal.Equals(existing.SourceFingerprint, sourceFingerprint)
                && StringComparer.Ordinal.Equals(existing.TargetFingerprint, targetFingerprint)
                && StringComparer.Ordinal.Equals(
                    existing.LocalStateFingerprint,
                    localStateFingerprint
                );
            return fingerprintsMatch
                ? new(existing, null)
                : new(
                    null,
                    "The source, target, or local state does not match the external cutover receipt."
                );
        }

        foreach (var table in tables)
        {
            var projection = await CutoverProjection.ReadAsync(
                target,
                null,
                table,
                null,
                cancellationToken
            );
            if (projection.Count != 0)
            {
                return new(
                    null,
                    "The PostgreSql target contains domain data but no matching external cutover receipt."
                );
            }
        }

        var receipt = new CutoverReceipt(
            CutoverReceipt.CurrentFormatVersion,
            requestedOperationId ?? Guid.NewGuid(),
            CutoverPhase.Prepared,
            sourceFingerprint,
            targetFingerprint,
            localStateFingerprint,
            [],
            null,
            null,
            DateTimeOffset.UtcNow,
            null
        );
        await store.WriteAsync(receipt, cancellationToken);
        return new(receipt, null);
    }
}
