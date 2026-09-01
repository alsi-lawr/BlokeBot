using BlokeBot.Hosting;
using BlokeBot.Persistence;
using Microsoft.Extensions.Configuration;
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

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.ToDictionary(static value => value.Key, static value => value.Value)
            )
            .Build();
}
