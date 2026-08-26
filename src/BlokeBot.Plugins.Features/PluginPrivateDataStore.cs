using System.Collections.Concurrent;
using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using Microsoft.Data.Sqlite;

namespace BlokeBot.Plugins.Features;

public enum PluginSqliteRejectionCode
{
    InvalidStatement,
    InvalidParameters,
    ResultTooLarge,
    StatementFailed,
    MigrationSessionRequired,
}

public abstract record PluginSqliteOutcome
{
    private PluginSqliteOutcome() { }

    public sealed record Changed(int Count) : PluginSqliteOutcome;

    public sealed record Rows(ImmutableArray<PluginValue.Map> Values) : PluginSqliteOutcome;

    public sealed record Rejected(PluginSqliteRejectionCode Code) : PluginSqliteOutcome;
}

public sealed partial class PluginPrivateDataStore(PluginPrivateDataOptions options)
{
    private const string _metadataTable = "__blokebot_plugin_metadata";
    private readonly ConcurrentDictionary<
        PluginWorkerInvocationId,
        PluginPrivateDataMigrationSession
    > _migrations = new();

    public ValueTask<PluginSqliteOutcome> ExecuteAsync(
        PluginWorkerInvocationIdentity identity,
        string sql,
        PluginValue.Map parameters,
        CancellationToken cancellationToken
    ) => ExecuteAsync(identity, sql, parameters, query: false, cancellationToken);

    public ValueTask<PluginSqliteOutcome> QueryAsync(
        PluginWorkerInvocationIdentity identity,
        string sql,
        PluginValue.Map parameters,
        CancellationToken cancellationToken
    ) => ExecuteAsync(identity, sql, parameters, query: true, cancellationToken);

    internal async ValueTask<PluginPrivateDataMigrationSession> BeginMigrationAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var connection = await OpenAsync(pluginId, cancellationToken);
        try
        {
            var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            var session = new PluginPrivateDataMigrationSession(this, connection, transaction);
            await session.InitializeAsync(_metadataTable, cancellationToken);
            return session;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal async ValueTask RemovePluginDataAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = options.DatabasePath(pluginId);
        foreach (var path in new[] { database, $"{database}-wal", $"{database}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        var directory = Path.GetDirectoryName(database)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }

        await ValueTask.CompletedTask;
    }

    internal void Bind(
        PluginWorkerInvocationId invocationId,
        PluginPrivateDataMigrationSession session
    )
    {
        if (!_migrations.TryAdd(invocationId, session))
        {
            throw new InvalidOperationException(
                "The plugin migration invocation is already bound."
            );
        }
    }

    internal void Unbind(
        PluginWorkerInvocationId invocationId,
        PluginPrivateDataMigrationSession session
    ) => _ = _migrations.TryRemove(new(invocationId, session));

    private async ValueTask<PluginSqliteOutcome> ExecuteAsync(
        PluginWorkerInvocationIdentity identity,
        string sql,
        PluginValue.Map parameters,
        bool query,
        CancellationToken cancellationToken
    )
    {
        if (!ValidSql(sql))
        {
            return Rejected(PluginSqliteRejectionCode.InvalidStatement);
        }

        if (identity.Context is PluginInvocationContext.Migration)
        {
            return _migrations.TryGetValue(identity.InvocationId, out var migration)
                ? await ExecuteAsync(
                    migration.Connection,
                    migration.Transaction,
                    sql,
                    parameters,
                    query,
                    cancellationToken
                )
                : Rejected(PluginSqliteRejectionCode.MigrationSessionRequired);
        }

        await using var connection = await OpenAsync(identity.Plugin.PluginId, cancellationToken);
        return await ExecuteAsync(
            connection,
            transaction: null,
            sql,
            parameters,
            query,
            cancellationToken
        );
    }

    private static async ValueTask<PluginSqliteOutcome> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        PluginValue.Map parameters,
        bool query,
        CancellationToken cancellationToken
    )
    {
        if (
            PluginPrivateDataConnectionGuard.ValidateStatement(connection, sql)
            == PluginPrivateStatementValidation.Restricted
        )
        {
            return Rejected(PluginSqliteRejectionCode.InvalidStatement);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 5;
        command.Transaction = transaction;
        if (!TryAddParameters(command, parameters))
        {
            return Rejected(PluginSqliteRejectionCode.InvalidParameters);
        }

        try
        {
            return query
                ? await QueryAsync(command, cancellationToken)
                : new PluginSqliteOutcome.Changed(
                    await command.ExecuteNonQueryAsync(cancellationToken)
                );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqliteException exception)
        {
            return Rejected(
                exception.SqliteErrorCode == SQLitePCL.raw.SQLITE_AUTH
                    ? PluginSqliteRejectionCode.InvalidStatement
                    : PluginSqliteRejectionCode.StatementFailed
            );
        }
    }

    private async ValueTask<SqliteConnection> OpenAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var path = options.DatabasePath(pluginId);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(options.ConnectionString(pluginId));
        await connection.OpenAsync(cancellationToken);
        PluginPrivateDataConnectionGuard.Apply(connection);
        return connection;
    }

    private static bool ValidSql(string? sql) =>
        sql is { Length: > 0 and <= PluginContractLimits.MaximumSqlCharacters }
        && !string.IsNullOrWhiteSpace(sql);

    private static bool TryAddParameters(SqliteCommand command, PluginValue.Map parameters)
    {
        if (parameters.Properties.Length > PluginContractLimits.MaximumSqlParameters)
        {
            return false;
        }

        foreach (var parameter in parameters.Properties)
        {
            if (!ValidParameterName(parameter.Name) || !TrySqlValue(parameter.Value, out var value))
            {
                return false;
            }

            _ = command.Parameters.AddWithValue($"${parameter.Name}", value);
        }

        return true;
    }

    private static bool ValidParameterName(string name) =>
        name is { Length: > 0 and <= 64 }
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool TrySqlValue(PluginValue value, out object result)
    {
        switch (value)
        {
            case PluginValue.Nil:
                result = DBNull.Value;
                return true;
            case PluginValue.Boolean boolean:
                result = boolean.Value ? 1L : 0L;
                return true;
            case PluginValue.Number number when double.IsFinite(number.Value):
                result = number.Value;
                return true;
            case PluginValue.String text:
                result = text.Value;
                return true;
            default:
                result = null!;
                return false;
        }
    }

    private static PluginSqliteOutcome.Rejected Rejected(PluginSqliteRejectionCode code) =>
        new(code);
}
