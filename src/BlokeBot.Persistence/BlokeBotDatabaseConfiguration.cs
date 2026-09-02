using System.Data.Common;
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
    internal const int PostgreSqlDefaultMaximumPoolSize = 20;
    internal const int PostgreSqlMaximumPoolSizeLimit = 50;
    internal const int PostgreSqlDefaultConnectionTimeoutSeconds = 15;
    internal const int PostgreSqlMaximumConnectionTimeoutSeconds = 30;
    internal const int PostgreSqlDefaultCommandTimeoutSeconds = 30;
    internal const int PostgreSqlMaximumCommandTimeoutSeconds = 60;
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
        DbConnectionStringBuilder explicitSettings;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
            explicitSettings = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new BlokeBotDatabaseConfigurationException(
                "The PostgreSql connection string is malformed."
            );
        }

        if (
            string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username)
        )
        {
            throw new BlokeBotDatabaseConfigurationException(
                "The PostgreSql connection string must specify Host, Database, and Username."
            );
        }

        if (Explicit(explicitSettings, "Maximum Pool Size", "MaxPoolSize"))
        {
            builder.MaxPoolSize = InRange(
                builder.MaxPoolSize,
                1,
                PostgreSqlMaximumPoolSizeLimit,
                "Maximum Pool Size"
            );
        }
        else if (builder.Pooling)
        {
            builder.MaxPoolSize = PostgreSqlDefaultMaximumPoolSize;
        }
        builder.Timeout = Explicit(explicitSettings, "Timeout", "Connection Timeout")
            ? InRange(builder.Timeout, 1, PostgreSqlMaximumConnectionTimeoutSeconds, "Timeout")
            : PostgreSqlDefaultConnectionTimeoutSeconds;
        builder.CommandTimeout = Explicit(explicitSettings, "Command Timeout", "CommandTimeout")
            ? InRange(
                builder.CommandTimeout,
                1,
                PostgreSqlMaximumCommandTimeoutSeconds,
                "Command Timeout"
            )
            : PostgreSqlDefaultCommandTimeoutSeconds;

        return new(BlokeBotDatabaseProvider.PostgreSql, builder.ConnectionString);
    }

    private static bool Explicit(DbConnectionStringBuilder builder, params string[] names) =>
        builder.Keys.Cast<string>().Select(Normalize).Intersect(names.Select(Normalize)).Any();

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int InRange(int value, int minimum, int maximum, string setting) =>
        value < minimum || value > maximum
            ? throw new BlokeBotDatabaseConfigurationException(
                $"The PostgreSql {setting} setting must be from {minimum} through {maximum}."
            )
            : value;

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
