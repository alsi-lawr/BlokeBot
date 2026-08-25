using BlokeBot.Plugins.Contracts;
using Microsoft.Data.Sqlite;

namespace BlokeBot.Plugins.Features;

internal sealed class PluginPrivateDataMigrationSession(
    PluginPrivateDataStore owner,
    SqliteConnection connection,
    SqliteTransaction transaction
) : IAsyncDisposable
{
    private bool _completed;

    internal SqliteConnection Connection { get; } = connection;

    internal SqliteTransaction Transaction { get; } = transaction;

    internal SemanticVersion? CurrentVersion { get; private set; }

    internal async ValueTask InitializeAsync(
        string metadataTable,
        CancellationToken cancellationToken
    )
    {
        await using var create = Connection.CreateCommand();
        create.Transaction = Transaction;
        create.CommandText =
            $"CREATE TABLE IF NOT EXISTS \"{metadataTable}\" (\"key\" TEXT PRIMARY KEY, \"value\" TEXT NOT NULL) WITHOUT ROWID;";
        _ = await create.ExecuteNonQueryAsync(cancellationToken);

        await using var read = Connection.CreateCommand();
        read.Transaction = Transaction;
        read.CommandText = $"SELECT \"value\" FROM \"{metadataTable}\" WHERE \"key\" = 'version';";
        var value = await read.ExecuteScalarAsync(cancellationToken) as string;
        CurrentVersion =
            value is null ? null
            : SemanticVersion.TryCreate(value, out var version) ? version
            : throw new InvalidDataException("The plugin private-data version is invalid.");
    }

    internal void Bind(PluginWorkerInvocationId invocationId) => owner.Bind(invocationId, this);

    internal void Unbind(PluginWorkerInvocationId invocationId) => owner.Unbind(invocationId, this);

    internal async ValueTask CommitAsync(
        SemanticVersion version,
        CancellationToken cancellationToken
    )
    {
        await using var write = Connection.CreateCommand();
        write.Transaction = Transaction;
        write.CommandText =
            "INSERT INTO \"__blokebot_plugin_metadata\" (\"key\", \"value\") VALUES ('version', $version) ON CONFLICT(\"key\") DO UPDATE SET \"value\" = excluded.\"value\";";
        _ = write.Parameters.AddWithValue("$version", version.Value);
        _ = await write.ExecuteNonQueryAsync(cancellationToken);
        await Transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await Transaction.RollbackAsync(CancellationToken.None);
        }
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
