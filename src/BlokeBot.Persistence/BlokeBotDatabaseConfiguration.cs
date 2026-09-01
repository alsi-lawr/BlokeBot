using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.Persistence;

public enum BlokeBotDatabaseProvider
{
    Sqlite,
    PostgreSql,
}

public sealed class BlokeBotDatabaseConfiguration
{
    internal const string PostgreSqlMigrationsAssembly = "BlokeBot.Persistence.PostgreSql";
    private readonly string _connectionString;

    private BlokeBotDatabaseConfiguration(
        BlokeBotDatabaseProvider provider,
        string connectionString
    )
    {
        Provider = provider;
        _connectionString = connectionString;
    }

    public BlokeBotDatabaseProvider Provider { get; }

    public static BlokeBotDatabaseConfiguration Sqlite(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
        }.ToString();
        return new(BlokeBotDatabaseProvider.Sqlite, connectionString);
    }

    public static BlokeBotDatabaseConfiguration PostgreSqlFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string connectionString;
        try
        {
            connectionString = File.ReadAllText(Path.GetFullPath(path)).Trim();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or System.Security.SecurityException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            throw new BlokeBotDatabaseConfigurationException(
                "The PostgreSql connection-string file could not be read."
            );
        }

        return PostgreSql(connectionString);
    }

    internal static BlokeBotDatabaseConfiguration PostgreSql(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new BlokeBotDatabaseConfigurationException(
                "The PostgreSql connection string is malformed."
            );
        }

        return
            string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username)
            ? throw new BlokeBotDatabaseConfigurationException(
                "The PostgreSql connection string must specify Host, Database, and Username."
            )
            : new(BlokeBotDatabaseProvider.PostgreSql, builder.ConnectionString);
    }

    internal void Configure(DbContextOptionsBuilder builder) =>
        _ = Provider switch
        {
            BlokeBotDatabaseProvider.Sqlite => builder
                .UseSqlite(_connectionString)
                .AddInterceptors(new WeeklyAnnouncementMigrationInterceptor()),
            BlokeBotDatabaseProvider.PostgreSql => builder.UseNpgsql(
                _connectionString,
                options => options.MigrationsAssembly(PostgreSqlMigrationsAssembly)
            ),
        };

    public BlokeBotDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<BlokeBotDbContext>();
        Configure(builder);
        return new BlokeBotDbContext(builder.Options);
    }
}

public sealed class BlokeBotDatabaseConfigurationException(string message) : Exception(message);
