using Microsoft.Data.Sqlite;

namespace BlokeBot.DatabaseCutover;

internal sealed class SqliteExclusiveLease : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteExclusiveLease(SqliteConnection connection) => _connection = connection;

    internal static async Task<SqliteExclusiveLease> AcquireAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var lockingMode = connection.CreateCommand();
        lockingMode.CommandText = "PRAGMA locking_mode = EXCLUSIVE;";
        var mode = Convert.ToString(
            await lockingMode.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture
        );
        if (!StringComparer.OrdinalIgnoreCase.Equals(mode, "exclusive"))
        {
            throw new InvalidOperationException("SQLite refused exclusive locking mode.");
        }

        await using var begin = connection.CreateCommand();
        begin.CommandText = "BEGIN EXCLUSIVE;";
        _ = await begin.ExecuteNonQueryAsync(cancellationToken);
        return new(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var rollback = _connection.CreateCommand();
        rollback.CommandText = "ROLLBACK;";
        _ = await rollback.ExecuteNonQueryAsync();
        await _connection.CloseAsync();
    }
}
