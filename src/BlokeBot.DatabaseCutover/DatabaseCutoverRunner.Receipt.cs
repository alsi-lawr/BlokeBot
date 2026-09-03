using Npgsql;

namespace BlokeBot.DatabaseCutover;

public sealed partial class DatabaseCutoverRunner
{
    private static async Task<CutoverReceiptResult> BindReceiptAsync(
        CutoverReceiptStore store,
        CutoverReceipt receipt,
        NpgsqlConnection target,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        if (receipt.Phase != CutoverPhase.SchemaReady)
        {
            return new(receipt, null);
        }

        foreach (var table in tables)
        {
            if (await CutoverSql.CountAsync(target, table, cancellationToken) != 0)
            {
                return new(null, "The PostgreSql target contains data before the copy phase.");
            }
        }

        var bound = receipt.WithPhase(CutoverPhase.Prepared);
        await store.WriteAsync(bound, cancellationToken);
        return new(bound, null);
    }
}
