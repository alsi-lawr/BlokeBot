using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace BlokeBot.Tests;

[NotInParallel]
public sealed class AutomationSourcePersistenceJourneys
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Sqlite_UpgradeAndRestart_PreserveHostAndSourceOccurrenceState(bool upgrade)
    {
        var directory = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-workspace",
            "314-persistence",
            Guid.NewGuid().ToString("N")
        );
        _ = Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(directory, "sources.db"),
                        Pooling = false,
                    }.ToString()
                )
                .AddInterceptors(new WeeklyAnnouncementMigrationInterceptor())
                .Options;
            await JourneyAsync(options, upgrade, "20261002145301_FullOverlayProtectedKeys");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test, DatabaseCutoverIntegration]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostgreSql_UpgradeAndRestart_PreserveHostAndSourceOccurrenceState(
        bool upgrade
    )
    {
        await using var postgres =
            await DatabaseCutoverIntegrationFixture.DisposablePostgreSql.StartAsync();
        await using (var connection = new NpgsqlConnection(postgres.AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = "CREATE DATABASE automation_sources_fixture";
            _ = await create.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
            .UseNpgsql(
                postgres.ApplicationConnectionString("automation_sources_fixture", "postgres"),
                provider => provider.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
            )
            .Options;
        await JourneyAsync(options, upgrade, "20261002151335_FullOverlayProtectedKeys");
    }

    private static async Task JourneyAsync(
        DbContextOptions<BlokeBotDbContext> options,
        bool upgrade,
        string baseline
    )
    {
        int hostId;
        await using (var before = new BlokeBotDbContext(options))
        {
            if (upgrade)
            {
                await before.Database.MigrateAsync(baseline);
                _ = await before.Database.ExecuteSqlRawAsync(
                    "INSERT INTO hosts (\"TwitchUserId\",\"Login\",\"DisplayName\",\"EnabledFeatures\",\"CreatedAtUtc\",\"BotRuntimeState\",\"ViewerPassportContinuityGeneration\",\"AutomationGeneration\",\"CommandsAliasesConfigured\") VALUES ('source-host-id','source-host','Source host',0,'2026-10-03 12:00:00',0,0,0,false)"
                );
            }
            await before.Database.MigrateAsync();
            (await before.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            var host = await before.Hosts.SingleOrDefaultAsync();
            if (host is null)
            {
                host = new()
                {
                    TwitchUserId = "source-host-id",
                    Login = "source-host",
                    DisplayName = "Source host",
                    EnabledFeatures = HostFeatureFlags.Automations,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _ = before.Hosts.Add(host);
                _ = await before.SaveChangesAsync();
            }
            hostId = host.Id;
            _ = before.AutomationSourceAdmissions.Add(
                new()
                {
                    HostId = hostId,
                    AcceptEventsAfterUtc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
                }
            );
            _ = before.AutomationCountdowns.Add(
                new()
                {
                    HostId = hostId,
                    Name = "tea",
                    OccurrenceId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                    ProcessId = Guid.NewGuid(),
                    IsRunning = true,
                    StartedAtUtc = DateTime.UtcNow,
                    ObservedAtUtc = DateTime.UtcNow,
                    DeadlineUtc = DateTime.UtcNow.AddMinutes(3),
                }
            );
            _ = before.AutomationStreamObservations.Add(
                new()
                {
                    HostId = hostId,
                    StreamId = "current-stream",
                    StartedAtUtc = DateTime.UtcNow,
                    ObservedAtUtc = DateTime.UtcNow,
                }
            );
            _ = before.AutomationSeenViewers.Add(new() { HostId = hostId, ViewerId = "viewer-id" });
            _ = before.AutomationGoalObservations.Add(
                new()
                {
                    HostId = hostId,
                    GoalId = "goal-id",
                    CurrentAmount = 100,
                    ObservedAtUtc = DateTime.UtcNow,
                    ConnectionId = Guid.NewGuid(),
                }
            );
            _ = before.AutomationGoalMilestones.Add(
                new()
                {
                    HostId = hostId,
                    GoalId = "goal-id",
                    Amount = 100,
                }
            );
            _ = await before.SaveChangesAsync();
        }
        await using (var restart = new BlokeBotDbContext(options))
        {
            (await restart.AutomationSourceAdmissions.SingleAsync()).AcceptEventsAfterUtc.ShouldBe(
                new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc)
            );
            (await restart.AutomationCountdowns.SingleAsync()).OccurrenceId.ShouldBe(
                Guid.Parse("77777777-7777-7777-7777-777777777777")
            );
            (await restart.AutomationSeenViewers.SingleAsync()).ViewerId.ShouldBe("viewer-id");
            (await restart.AutomationStreamObservations.SingleAsync()).StreamId.ShouldBe(
                "current-stream"
            );
            (await restart.AutomationGoalMilestones.SingleAsync()).Amount.ShouldBe(100);
            _ = await restart
                .AutomationCountdowns.Where(t => t.HostId == hostId && t.IsRunning)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.IsRunning, false).SetProperty(t => t.WasCancelled, true)
                );
            _ = await restart
                .AutomationStreamObservations.Where(t => t.HostId == hostId)
                .ExecuteDeleteAsync();
        }
        await using var verified = new BlokeBotDbContext(options);
        var cancelled = await verified.AutomationCountdowns.SingleAsync();
        cancelled.IsRunning.ShouldBeFalse();
        cancelled.WasCancelled.ShouldBeTrue();
        (await verified.AutomationSeenViewers.AnyAsync()).ShouldBeFalse();
        (await verified.AutomationGoalMilestones.SingleAsync()).Amount.ShouldBe(100);
        (await verified.Hosts.SingleAsync()).Login.ShouldBe("source-host");
    }
}
