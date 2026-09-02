using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace BlokeBot.DatabaseCutover;

internal static class CutoverFingerprint
{
    internal static async Task<string> SourceAsync(
        DbConnection connection,
        DbTransaction? transaction,
        IReadOnlyList<string> migrations,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, string.Join('\n', migrations));
        foreach (var table in tables)
        {
            var projection = await CutoverProjection.ReadAsync(
                connection,
                transaction,
                table,
                null,
                cancellationToken
            );
            Append(hash, table.Name);
            Append(
                hash,
                projection.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            Append(hash, projection.Hash);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static async Task<string> TargetIdentityAsync(
        DbConnection connection,
        string clusterIdentity,
        IReadOnlyList<string> migrations,
        IReadOnlyList<CutoverTable> tables,
        CancellationToken cancellationToken
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT current_database(), database.oid::text FROM pg_database AS database WHERE database.datname = current_database();";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The PostgreSql database identity is unavailable.");
        }

        Append(hash, clusterIdentity);
        Append(hash, reader.GetString(0));
        Append(hash, reader.GetString(1));
        Append(hash, string.Join('\n', migrations));
        foreach (var table in tables)
        {
            Append(hash, table.Name);
            foreach (var column in table.Columns)
            {
                Append(hash, column.Name);
                Append(hash, column.TargetStoreType);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string Verification(
        IEnumerable<(string Table, TableProjection Projection)> projections
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (table, projection) in projections)
        {
            Append(hash, table);
            Append(
                hash,
                projection.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            Append(hash, projection.Hash);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        _ = BitConverter.TryWriteBytes(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
