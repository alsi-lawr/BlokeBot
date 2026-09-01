using System.Data.Common;
using System.Security.Cryptography;
using Npgsql;

namespace BlokeBot.DatabaseCutover;

internal sealed record TableProjection(long Count, string Hash);

internal static class CutoverProjection
{
    internal static async Task<TableProjection> ReadAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CutoverTable table,
        long? rowLimit,
        CancellationToken cancellationToken,
        bool stageSelfReferences = false
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SelectSql(table, connection is NpgsqlConnection, rowLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            foreach (var column in table.Columns)
            {
                CutoverValues.AppendCanonical(
                    hash,
                    stageSelfReferences && table.SelfReferenceColumns.Contains(column.Name) ? null
                        : reader.IsDBNull(reader.GetOrdinal(column.Name)) ? null
                        : reader.GetValue(reader.GetOrdinal(column.Name)),
                    column.TargetStoreType
                );
            }

            count++;
        }

        return new TableProjection(count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    internal static string SelectSql(CutoverTable table, bool postgreSql, long? rowLimit = null)
    {
        var columns = string.Join(", ", table.Columns.Select(column => Quote(column.Name)));
        var limit = rowLimit is null ? string.Empty : $" LIMIT {rowLimit.Value}";
        return $"SELECT {columns} FROM {Quote(table.Name)} ORDER BY {OrderBy(table, postgreSql)}{limit};";
    }

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

    internal static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
