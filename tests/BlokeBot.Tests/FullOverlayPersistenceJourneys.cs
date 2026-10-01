using BlokeBot.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlokeBot.Tests;

public enum FullOverlayMigrationStart
{
    Empty,
    Released016,
}

public sealed partial class FullOverlayPersistenceJourneys
{
    [Test]
    [Arguments(FullOverlayMigrationStart.Empty)]
    [Arguments(FullOverlayMigrationStart.Released016)]
    public async Task Sqlite_FullLifecycleAndSimpleLiveSaveSurviveRestart(
        FullOverlayMigrationStart start
    )
    {
        var directory = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-workspace",
            "overlay-composition-20261001",
            "285",
            "provider-fixtures",
            Guid.NewGuid().ToString("N")
        );
        _ = Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(directory, "fixture.db"),
                        Pooling = false,
                    }.ToString()
                )
                .AddInterceptors(new WeeklyAnnouncementMigrationInterceptor())
                .Options;
            await JourneyAsync(new(options), start, "20260907091826_CurrentSubflowCallers");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test, DatabaseCutoverIntegration]
    [Arguments(FullOverlayMigrationStart.Empty)]
    [Arguments(FullOverlayMigrationStart.Released016)]
    public async Task PostgreSql_FullLifecycleAndSimpleLiveSaveSurviveRestart(
        FullOverlayMigrationStart start
    )
    {
        await using var postgres =
            await DatabaseCutoverIntegrationFixture.DisposablePostgreSql.StartAsync();
        await using (var connection = new NpgsqlConnection(postgres.AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = "CREATE DATABASE full_overlay_fixture";
            _ = await create.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseNpgsql(
                postgres.ApplicationConnectionString("full_overlay_fixture", "postgres"),
                provider => provider.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
            )
            .Options;
        await JourneyAsync(new(options), start, "20260907093259_CurrentSubflowCallers");
    }
}
