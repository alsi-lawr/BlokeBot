using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReceiptResult> BindReceiptAsync(
        CutoverReceiptStore store,
        CutoverReceipt receipt,
        string targetFingerprint,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        if (receipt.TargetFingerprint is not null)
        {
            return StringComparer.Ordinal.Equals(receipt.TargetFingerprint, targetFingerprint)
                ? new(receipt, null)
                : new(null, "The PostgreSql target does not match the external cutover receipt.");
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
                return new(null, "The PostgreSql target contains data before the copy phase.");
            }
        }

        var bound = receipt.Prepared(targetFingerprint);
        await store.WriteAsync(bound, cancellationToken);
        return new(bound, null);
    }
}
