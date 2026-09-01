using System.Data.Common;
using System.Globalization;
using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.DatabaseWorkloads;

internal sealed class SqliteWorkloadDatabase : WorkloadDatabase
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    internal SqliteWorkloadDatabase(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        RefuseExisting(_databasePath);
        var parent =
            Path.GetDirectoryName(_databasePath)
            ?? throw new IOException("The database path must have a parent directory.");
        _ = Directory.CreateDirectory(parent);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 0,
        }.ToString();
    }

    internal override string Provider => "sqlite";

    internal override Task PrepareRunAsync(int run, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (run > 0)
        {
            DeleteOwnedDatabase(_databasePath);
        }
        return Task.CompletedTask;
    }

    internal override async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 0;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    internal override Task<DbTransaction> BeginWriteAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<DbTransaction>(
            ((SqliteConnection)connection).BeginTransaction(deferred: false)
        );
    }

    internal override void Configure(DbContextOptionsBuilder<BlokeBotDbContext> options) =>
        _ = options.UseSqlite(_connectionString);

    internal override bool IsRetryableContention(Exception exception) =>
        MainDatabaseFailureClassifier.IsContention(exception);

    internal override string InsertIgnore(string sqlite, string postgreSql) => sqlite;

    internal override string CommandText(string sql) => sql;

    internal override string ParameterName(string name) => name;

    internal override async Task<string> ReadVersionAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var configure = connection.CreateCommand();
        configure.CommandText = "PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0;";
        _ = await configure.ExecuteNonQueryAsync(cancellationToken);
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT sqlite_version();";
        return Convert.ToString(
                await version.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture
            ) ?? string.Empty;
    }

    internal override Task<StorageResult> ReadStorageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var databaseBytes = FileLength(_databasePath);
        var walBytes = FileLength(_databasePath + "-wal");
        return Task.FromResult(
            new StorageResult(databaseBytes, walBytes, databaseBytes + walBytes)
        );
    }

    internal override string Explain(string sql) => "EXPLAIN QUERY PLAN " + sql;

    internal override string ReadPlanStep(DbDataReader reader) => reader.GetString(3);

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static void RefuseExisting(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (
            File.Exists(fullPath)
            || File.Exists(fullPath + "-wal")
            || File.Exists(fullPath + "-shm")
        )
        {
            throw new IOException(
                "The SQLite baseline requires a new database path and never overwrites an existing database."
            );
        }
    }

    private static long FileLength(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void DeleteOwnedDatabase(string path)
    {
        foreach (var ownedPath in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(ownedPath))
            {
                File.Delete(ownedPath);
            }
        }
    }
}

public static class SqliteBaselineSafety
{
    public static void RefuseExisting(string databasePath) =>
        SqliteWorkloadDatabase.RefuseExisting(databasePath);
}
