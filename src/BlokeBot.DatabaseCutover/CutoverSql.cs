using System.Data.Common;
using System.Globalization;

namespace BlokeBot.DatabaseCutover;

internal sealed record CutoverTableRows(string Table, long Rows);

internal static class CutoverSql
{
    internal static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    internal static string OrderBy(CutoverTable table, bool postgreSql) =>
        string.Join(", ", table.KeyColumns.Select(key => OrderKey(table, key, postgreSql)));

    private static string OrderKey(CutoverTable table, string key, bool postgreSql)
    {
        var storeType = table.Columns.Single(column => column.Name == key).TargetStoreType;
        return storeType == "uuid"
            ? postgreSql
                ? $"{Quote(key)}::text COLLATE \"C\""
                : $"lower({Quote(key)}) COLLATE BINARY"
            : storeType == "text"
            || storeType.StartsWith("character varying", StringComparison.Ordinal)
                ? postgreSql
                    ? $"{Quote(key)} COLLATE \"C\""
                    : $"{Quote(key)} COLLATE BINARY"
                : Quote(key);
    }

    internal static async Task<long> CountAsync(
        DbConnection connection,
        CutoverTable table,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {Quote(table.Name)};";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
    }

    internal static async Task<IReadOnlyList<CutoverTableRows>> CountAllAsync(
        DbConnection connection,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<CutoverTableRows>(tables.Count);
        foreach (var table in tables)
        {
            rows.Add(new(table.Name, await CountAsync(connection, table, cancellationToken)));
        }

        return rows;
    }
}
