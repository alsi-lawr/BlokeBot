using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationRunLimitsMigrationTests
{
    [Test]
    public async Task RunLimits_SqliteLegacyUpgradeLeavesOldAndNewSettingsUnlimitedAndRoundTripsLimits()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using var db = await database.CreateDbContextAsync();
        await Upgrade(db, "20261015232145_AutomationTraceOutcomeSummary");
    }

    [Test, Explicit]
    public async Task RunLimits_PostgreSqlLegacyUpgradeLeavesOldAndNewSettingsUnlimitedAndRoundTripsLimits()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await using var initial = database.CreateDbContext();
        await using var db = new BlokeBotDbContext(
            new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseNpgsql(
                    initial.Database.GetConnectionString()!,
                    options => options.MigrationsAssembly("BlokeBot.Persistence.PostgreSql")
                )
                .Options
        );
        _ = await db.Database.EnsureDeletedAsync();
        await Upgrade(db, "20261015232213_AutomationTraceOutcomeSummary");
    }

    private static async Task Upgrade(BlokeBotDbContext db, string previous)
    {
        await db.Database.MigrateAsync(previous);
        var now = DateTime.UtcNow;
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO hosts ("Login","DisplayName","BotRuntimeState","EnabledFeatures","ViewerPassportContinuityGeneration","AutomationGeneration","TimeZoneId","CommandsAliasesConfigured","CreatedAtUtc")
            VALUES ({"legacy-limits"},{"Legacy"},{0},{0L},{0},{0},{"UTC"},{false},{now})
            """
        );
        var host = await db.Hosts.Select(value => value.Id).SingleAsync();
        var id = Guid.NewGuid();
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO automation_flows ("Id","HostId","Name","SchemaVersion","IsEnabled","UseVerticalLayout","UseSmoothEdges","CreatedAtUtc","UpdatedAtUtc")
            VALUES ({id},{host},{"Legacy flow"},{1},{false},{false},{false},{now},{now})
            """
        );
        await db.Database.MigrateAsync();
        var legacyHost = await db.Hosts.SingleAsync();
        var legacyFlow = await db.AutomationFlows.SingleAsync();
        legacyHost.MaximumUnfinishedAutomationRuns.ShouldBeNull();
        legacyHost.MaximumWaitingAutomationRuns.ShouldBeNull();
        legacyFlow.MaximumUnfinishedRuns.ShouldBeNull();
        legacyFlow.MaximumWaitingRuns.ShouldBeNull();
        legacyHost.MaximumUnfinishedAutomationRuns = 5;
        legacyHost.MaximumWaitingAutomationRuns = 3;
        legacyFlow.MaximumUnfinishedRuns = 2;
        legacyFlow.MaximumWaitingRuns = 1;
        _ = await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.Hosts.SingleAsync()).MaximumUnfinishedAutomationRuns.ShouldBe(5);
        (await db.Hosts.SingleAsync()).MaximumWaitingAutomationRuns.ShouldBe(3);
        (await db.AutomationFlows.SingleAsync()).MaximumUnfinishedRuns.ShouldBe(2);
        (await db.AutomationFlows.SingleAsync()).MaximumWaitingRuns.ShouldBe(1);
        var added = new BlokeBot.Persistence.Models.AutomationFlow
        {
            Id = Guid.NewGuid(),
            HostId = host,
            Name = "New",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.AutomationFlows.Add(added);
        _ = await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var unlimited = await db.AutomationFlows.SingleAsync(value => value.Id == added.Id);
        unlimited.MaximumUnfinishedRuns.ShouldBeNull();
        unlimited.MaximumWaitingRuns.ShouldBeNull();
    }
}
