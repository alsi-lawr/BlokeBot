using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

internal static class HetznerBaselineBridge
{
    private const string _baselineMigrationId = "20260722162847_20260722_InitialHetznerBaseline";
    private const string _baselineProductVersion = "10.0.9";
    private const string _baselineSchemaSignature =
        "c1d341153310cd5c13838e8535e79d2035c4918267e9ed0d104e7e568b3bf03f";

    internal static async Task ApplyAsync(BlokeBotDbContext db, CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        if (
            await HasMigrationsHistoryAsync(connection, cancellationToken)
            || !await HasExistingSchemaAsync(connection, cancellationToken)
        )
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );
        var signature = await CalculateSchemaSignatureAsync(
            connection,
            (SqliteTransaction)transaction,
            cancellationToken
        );
        if (!StringComparer.Ordinal.Equals(signature, _baselineSchemaSignature))
        {
            throw new UnsupportedDatabaseBaselineException();
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );

            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ($migrationId, $productVersion);
            """;
        command.Parameters.AddWithValue("$migrationId", _baselineMigrationId);
        command.Parameters.AddWithValue("$productVersion", _baselineProductVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> HasMigrationsHistoryAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE type = 'table' AND name = '__EFMigrationsHistory'
            );
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<bool> HasExistingSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_schema
                WHERE name NOT LIKE 'sqlite_%'
                    AND name <> '__EFMigrationsHistory'
            );
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<string> CalculateSchemaSignatureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT type, name, sql
            FROM sqlite_schema
            WHERE name NOT LIKE 'sqlite_%'
                AND name <> '__EFMigrationsHistory'
            ORDER BY type, name;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var schema = new StringBuilder();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (schema.Length > 0)
            {
                schema.Append('\n');
            }

            var type = reader.GetString(0).ToLowerInvariant();
            var name = reader.GetString(1).ToLowerInvariant();
            if (type is not ("table" or "index") || reader.IsDBNull(2))
            {
                throw new UnsupportedDatabaseBaselineException();
            }

            var sql = reader.GetString(2);
            schema.Append(type).Append(':').Append(name).Append(':');
            if (type == "table")
            {
                EnsureTableHasNoSuffix(sql);
                var definitions = SplitTableDefinitions(sql)
                    .Select(NormalizeSql)
                    .Order(StringComparer.Ordinal);
                schema.AppendJoin(',', definitions);
            }
            else
            {
                schema.Append(NormalizeSql(RemoveIfNotExists(sql)));
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(schema.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    private static IEnumerable<string> SplitTableDefinitions(string sql)
    {
        var start = sql.IndexOf('(');
        var end = sql.LastIndexOf(')');
        var definition = sql[(start + 1)..end];
        var segmentStart = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < definition.Length; index++)
        {
            var character = definition[index];
            if (quote != '\0')
            {
                if (character != quote)
                {
                    continue;
                }

                if (index + 1 < definition.Length && definition[index + 1] == quote)
                {
                    index++;
                }
                else
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"' or '`')
            {
                quote = character;
            }
            else if (character == '[')
            {
                quote = ']';
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                yield return definition[segmentStart..index].ToString();
                segmentStart = index + 1;
            }
        }

        yield return definition[segmentStart..].ToString();
    }

    private static void EnsureTableHasNoSuffix(string sql)
    {
        if (!string.IsNullOrWhiteSpace(sql[(sql.LastIndexOf(')') + 1)..]))
        {
            throw new UnsupportedDatabaseBaselineException();
        }
    }

    private static string NormalizeSql(string sql)
    {
        var normalized = new StringBuilder(sql.Length);
        var inStringLiteral = false;
        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            if (inStringLiteral)
            {
                normalized.Append(character);
                if (character != '\'')
                {
                    continue;
                }

                if (index + 1 < sql.Length && sql[index + 1] == '\'')
                {
                    normalized.Append(sql[++index]);
                }
                else
                {
                    inStringLiteral = false;
                }

                continue;
            }

            if (character == '\'')
            {
                inStringLiteral = true;
                normalized.Append(character);
            }
            else if (character is not ('"' or '`' or '[' or ']') && !char.IsWhiteSpace(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString();
    }

    private static string RemoveIfNotExists(string sql)
    {
        const string Clause = "IF NOT EXISTS";
        var start = sql.IndexOf(Clause, StringComparison.OrdinalIgnoreCase);
        return start < 0 ? sql : sql.Remove(start, Clause.Length);
    }
}
