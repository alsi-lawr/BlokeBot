using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.Persistence;

public sealed class BlokeBotDatabaseRuntimeLease : IAsyncDisposable
{
    internal const long OwnershipLockKey = 0x424C4F4B45424F54;

    private readonly BlokeBotDbContext? _db;
    private readonly NpgsqlConnection? _connection;

    private BlokeBotDatabaseRuntimeLease(BlokeBotDbContext? db, NpgsqlConnection? connection)
    {
        _db = db;
        _connection = connection;
    }

    public static async Task<BlokeBotDatabaseRuntimeLease> AcquireAsync(
        BlokeBotDatabaseConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Provider == BlokeBotDatabaseProvider.Sqlite)
        {
            return new(null, null);
        }

        var db = configuration.CreateDbContext();
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock_shared(@lock_key);";
            _ = command.Parameters.AddWithValue("lock_key", OwnershipLockKey);
            var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            return acquired
                ? new(db, connection)
                : throw new BlokeBotDatabaseOwnershipException(
                    "The PostgreSql database is reserved by an offline database operation."
                );
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock_shared(@lock_key);";
                _ = command.Parameters.AddWithValue("lock_key", OwnershipLockKey);
                _ = await command.ExecuteScalarAsync();
            }
        }
        finally
        {
            await _connection.DisposeAsync();
            await _db!.DisposeAsync();
        }
    }
}

public sealed class BlokeBotDatabaseOwnershipException(string message) : Exception(message);
