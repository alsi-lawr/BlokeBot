using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationViewerCountdownMigrationTests
{
    [Test]
    public async Task ViewerCountdown_SqliteUpgradePreservesFixedIdentityAndRunLimits()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using var db = await database.CreateDbContextAsync();
        await Upgrade(db, "20261016034612_AutomationRunLimits");
    }

    [Test, Explicit]
    public async Task ViewerCountdown_PostgreSqlUpgradePreservesFixedIdentityAndRunLimits()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await using var initial = await database.CreateDbContextAsync();
        await using var db = new BlokeBotDbContext(
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseNpgsql(
                    initial.Database.GetConnectionString()!,
                    options => options.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
                )
                .Options
        );
        _ = await db.Database.EnsureDeletedAsync();
        await Upgrade(db, "20261016034651_AutomationRunLimits");
    }

    private static async Task Upgrade(BlokeBotDbContext db, string previous)
    {
        await db.Database.MigrateAsync(previous);
        var now = DateTime.UtcNow;
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO hosts ("Login","DisplayName","BotRuntimeState","EnabledFeatures","ViewerPassportContinuityGeneration","AutomationGeneration","TimeZoneId","CommandsAliasesConfigured","CreatedAtUtc","MaximumUnfinishedAutomationRuns","MaximumWaitingAutomationRuns")
            VALUES ({"legacy-countdown"},{"Legacy"},{0},{0L},{0},{0},{"UTC"},{false},{now},{9},{4})
            """
        );
        var host = await db.Hosts.Select(value => value.Id).SingleAsync();
        var occurrence = Guid.NewGuid();
        var process = Guid.NewGuid();
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO automation_countdowns ("HostId","Name","OccurrenceId","ProcessId","AutomationGeneration","IsRunning","WasCancelled","StartedAtUtc","DeadlineUtc","ObservedAtUtc")
            VALUES ({host},{"tea"},{occurrence},{process},{0},{true},{false},{now},{now.AddSeconds(
                10
            )},{now})
            """
        );
        await db.Database.MigrateAsync();
        var timer = await db.AutomationCountdowns.SingleAsync();
        timer.OwnerViewerId.ShouldBeEmpty();
        timer.OwnerDisplayName.ShouldBeNull();
        timer.OccurrenceId.ShouldBe(occurrence);
        timer.ProcessId.ShouldBe(process);
        var settings = await db.Hosts.SingleAsync();
        settings.MaximumUnfinishedAutomationRuns.ShouldBe(9);
        settings.MaximumWaitingAutomationRuns.ShouldBe(4);
        _ = db.AutomationCountdowns.Add(
            new()
            {
                HostId = host,
                Name = "tea",
                OwnerViewerId = "viewer",
                OwnerDisplayName = "Display",
                OccurrenceId = Guid.NewGuid(),
                ProcessId = process,
                IsRunning = true,
                StartedAtUtc = now,
                DeadlineUtc = now.AddSeconds(20),
                ObservedAtUtc = now,
            }
        );
        _ = await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.AutomationCountdowns.CountAsync()).ShouldBe(2);
        (
            await db.AutomationCountdowns.SingleAsync(value => value.OwnerViewerId == "viewer")
        ).OwnerDisplayName.ShouldBe("Display");
    }
}
