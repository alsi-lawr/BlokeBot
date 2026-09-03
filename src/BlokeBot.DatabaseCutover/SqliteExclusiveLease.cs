using Microsoft.Data.Sqlite;

namespace BlokeBot.DatabaseCutover;

internal sealed class SqliteExclusiveLease : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteExclusiveLease(SqliteConnection connection) => _connection = connection;

    // The exclusive transaction is the lease: it holds the SQLite write lock until DisposeAsync
    // rolls back and closes the connection, so no other connection can write during the cutover.
    // Do not set locking_mode = EXCLUSIVE before it. In WAL mode that mode escalates the next
    // write-transaction start to an EXCLUSIVE file lock, which fails while any connection that
    // has opened the WAL, in this process or another, still holds its SHARED lock.
    internal static async Task<SqliteExclusiveLease> AcquireAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken
    )
    {
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
