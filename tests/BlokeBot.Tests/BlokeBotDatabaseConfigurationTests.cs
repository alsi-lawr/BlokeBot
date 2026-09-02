using BlokeBot.Hosting;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Shouldly;

namespace BlokeBot.Tests;

public sealed class BlokeBotDatabaseConfigurationTests
{
    [Test]
    public void PostgreSqlSecretFile_Selecting_CreatesNpgsqlContextWithoutLeakingSecret()
    {
        var file = Path.GetTempFileName();
        const string Secret = "database-password-value";
        File.WriteAllText(
            file,
            $"Host=database.internal;Database=blokebot;Username=blokebot;Password={Secret}"
        );
        try
        {
            var settings = BlokeBotMainDatabaseSettings.FromConfiguration(
                Configuration(
                    (BlokeBotMainDatabaseSettings.ProviderKey, "PostgreSql"),
                    (BlokeBotMainDatabaseSettings.PostgreSqlConnectionStringFileKey, file)
                )
            );
            var database = settings.CreateConfiguration(
                new BlokeBotStatePaths("/state", "/state/unused.db", "/state/tokens.json")
            );
            using var db = database.CreateDbContext();

            settings.Provider.ShouldBe(BlokeBotDatabaseProvider.PostgreSql);
            db.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
            database.ToString()!.ShouldNotContain(Secret);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void ContradictoryProviderSettings_Selecting_FailsBeforeDatabaseConstruction()
    {
        var exception = Should.Throw<BlokeBotHostStartupException>(() =>
            BlokeBotMainDatabaseSettings.FromConfiguration(
                Configuration(
                    (BlokeBotMainDatabaseSettings.ProviderKey, "PostgreSql"),
                    (BlokeBotMainDatabaseSettings.DatabasePathKey, "/state/blokebot.db"),
                    (
                        BlokeBotMainDatabaseSettings.PostgreSqlConnectionStringFileKey,
                        "/run/credentials/database"
                    )
                )
            )
        );

        exception.Summary.ShouldContain("cannot be combined");
    }

    [Test]
    public void IncompleteSecret_Selecting_FailsWithoutIncludingSecretContent()
    {
        var file = Path.GetTempFileName();
        const string Secret = "password-that-must-not-escape";
        File.WriteAllText(file, $"Password={Secret}");
        try
        {
            var settings = BlokeBotMainDatabaseSettings.FromConfiguration(
                Configuration(
                    (BlokeBotMainDatabaseSettings.ProviderKey, "PostgreSql"),
                    (BlokeBotMainDatabaseSettings.PostgreSqlConnectionStringFileKey, file)
                )
            );

            var exception = Should.Throw<BlokeBotHostStartupException>(() =>
                settings.CreateConfiguration(
                    new BlokeBotStatePaths("/state", "/state/unused.db", "/state/tokens.json")
                )
            );
            exception.Summary.ShouldContain("Host, Database, and Username");
            exception.Summary.ShouldNotContain(Secret);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void PostgreSqlConnectionBounds_DefaultsAcceptLimitsAndRejectsOutsideThem()
    {
        var defaultFile = ConnectionFile("Host=localhost;Database=blokebot;Username=blokebot");
        try
        {
            using var db = BlokeBotDatabaseConfiguration
                .PostgreSqlFromFile(defaultFile)
                .CreateDbContext();
            var defaults = new NpgsqlConnectionStringBuilder(
                db.Database.GetDbConnection().ConnectionString
            );
            defaults.MaxPoolSize.ShouldBe(20);
            defaults.Timeout.ShouldBe(15);
            defaults.CommandTimeout.ShouldBe(30);
        }
        finally
        {
            File.Delete(defaultFile);
        }

        AssertConnectionBounds(
            "Maximum Pool Size=1;Timeout=1;Command Timeout=1",
            maximumPoolSize: 1,
            connectionTimeout: 1,
            commandTimeout: 1
        );
        AssertConnectionBounds(
            "Maximum Pool Size=50;Timeout=30;Command Timeout=60",
            maximumPoolSize: 50,
            connectionTimeout: 30,
            commandTimeout: 60
        );

        foreach (
            var setting in new[]
            {
                "Maximum Pool Size=0",
                "Maximum Pool Size=51",
                "Pooling=false;Maximum Pool Size=51",
                "Timeout=0",
                "Timeout=31",
                "Command Timeout=0",
                "Command Timeout=61",
            }
        )
        {
            var file = ConnectionFile(
                $"Host=localhost;Database=blokebot;Username=blokebot;{setting}"
            );
            try
            {
                var exception = Should.Throw<BlokeBotDatabaseConfigurationException>(() =>
                    BlokeBotDatabaseConfiguration.PostgreSqlFromFile(file)
                );
                exception.Message.ShouldContain("must be from");
            }
            finally
            {
                File.Delete(file);
            }
        }
    }

    private static void AssertConnectionBounds(
        string settings,
        int maximumPoolSize,
        int connectionTimeout,
        int commandTimeout
    )
    {
        var file = ConnectionFile($"Host=localhost;Database=blokebot;Username=blokebot;{settings}");
        try
        {
            using var db = BlokeBotDatabaseConfiguration.PostgreSqlFromFile(file).CreateDbContext();
            var actual = new NpgsqlConnectionStringBuilder(
                db.Database.GetDbConnection().ConnectionString
            );
            actual.MaxPoolSize.ShouldBe(maximumPoolSize);
            actual.Timeout.ShouldBe(connectionTimeout);
            actual.CommandTimeout.ShouldBe(commandTimeout);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string ConnectionFile(string connectionString)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, connectionString);
        return path;
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.ToDictionary(static value => value.Key, static value => value.Value)
            )
            .Build();
}
