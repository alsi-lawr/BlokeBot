using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.DatabaseWorkloads;

internal sealed class PostgreSqlWorkloadDatabase : WorkloadDatabase
{
    private const string _schema = "blokebot_workload_v1";
    private readonly string _administrativeConnectionString;
    private readonly string _workloadConnectionString;
    private bool _ownsSchema;

    internal PostgreSqlWorkloadDatabase(string connectionString)
    {
        var administrative = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
        };
        _administrativeConnectionString = administrative.ConnectionString;
        administrative.SearchPath = _schema;
        _workloadConnectionString = administrative.ConnectionString;
    }

    internal override string Provider => "postgresql";

    internal override async Task PrepareRunAsync(int run, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_administrativeConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (run == 0)
        {
            await using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = $1);";
            _ = exists.Parameters.AddWithValue(_schema);
            if (await exists.ExecuteScalarAsync(cancellationToken) is true)
            {
                throw new IOException(
                    $"The PostgreSQL workload schema '{_schema}' already exists and will not be overwritten."
                );
            }
        }
        else
        {
            await DropOwnedSchemaAsync(connection, cancellationToken);
        }

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE SCHEMA \"{_schema}\";";
        _ = await create.ExecuteNonQueryAsync(cancellationToken);
        _ownsSchema = true;
    }

    internal override async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_workloadConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    internal override async Task<DbTransaction> BeginWriteAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    ) =>
        await ((NpgsqlConnection)connection).BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

    internal override void Configure(DbContextOptionsBuilder<BlokeBotDbContext> options) =>
        _ = options.UseNpgsql(_workloadConnectionString);

    internal override bool IsRetryableContention(Exception exception) =>
        MainDatabaseFailureClassifier.IsContention(exception);

    internal override string InsertIgnore(string sqlite, string postgreSql) => postgreSql;

    internal override string CommandText(string sql) =>
        Regex.Replace(sql, @"\$([A-Za-z][A-Za-z0-9_]*)", "@$1");

    internal override string ParameterName(string name) => name.TrimStart('$');

    internal override async Task<string> ReadVersionAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW server_version;";
        return Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            ) ?? string.Empty;
    }

    internal override async Task<StorageResult> ReadStorageAsync(
        CancellationToken cancellationToken
    )
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(SUM(pg_total_relation_size(format('%I.%I', schemaname, tablename)::regclass)), 0) FROM pg_tables WHERE schemaname = current_schema();";
        var bytes = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );
        return new(bytes, 0, bytes);
    }

    internal override string Explain(string sql) => "EXPLAIN " + sql;

    internal override string ReadPlanStep(DbDataReader reader) => reader.GetString(0);

    public override async ValueTask DisposeAsync()
    {
        if (!_ownsSchema)
        {
            return;
        }
        await using var connection = new NpgsqlConnection(_administrativeConnectionString);
        await connection.OpenAsync();
        await DropOwnedSchemaAsync(connection, CancellationToken.None);
        _ownsSchema = false;
    }

    private static async Task DropOwnedSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP SCHEMA \"{_schema}\" CASCADE;";
        _ = await drop.ExecuteNonQueryAsync(cancellationToken);
    }
}
