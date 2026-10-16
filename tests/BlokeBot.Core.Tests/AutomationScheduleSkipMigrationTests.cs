using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationScheduleSkipMigrationTests
{
    [Test]
    public async Task ScheduleSkips_SqliteUpgradePreservesViewerIdentityAndLimits()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        await using var db = await database.CreateDbContextAsync();
        await Upgrade(db, "20261016041853_ViewerOwnedCountdowns");
    }

    [Test, Explicit]
    public async Task ScheduleSkips_PostgreSqlUpgradePreservesViewerIdentityAndLimits()
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
        await Upgrade(db, "20261016041929_ViewerOwnedCountdowns");
    }

    private static async Task Upgrade(BlokeBotDbContext db, string previous)
    {
        await db.Database.MigrateAsync(previous);
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO hosts ("Login","DisplayName","BotRuntimeState","EnabledFeatures","ViewerPassportContinuityGeneration","AutomationGeneration","TimeZoneId","CommandsAliasesConfigured","CreatedAtUtc","MaximumUnfinishedAutomationRuns","MaximumWaitingAutomationRuns")
            VALUES ({"schedule-legacy"},{"Legacy"},{0},{0L},{0},{0},{"UTC"},{false},{now},{9},{4})
            """
        );
        var host = await db.Hosts.Select(value => value.Id).SingleAsync();
        var occurrence = Guid.NewGuid();
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO automation_countdowns ("HostId","Name","OwnerViewerId","OwnerDisplayName","OccurrenceId","ProcessId","AutomationGeneration","IsRunning","WasCancelled","StartedAtUtc","DeadlineUtc","ObservedAtUtc")
            VALUES ({host},{"tea"},{"viewer"},{"Display"},{occurrence},{Guid.NewGuid()},{0},{true},{false},{now},{now.AddSeconds(
                10
            )},{now})
            """
        );
        await db.Database.MigrateAsync();
        (await db.AutomationCountdowns.SingleAsync()).OwnerViewerId.ShouldBe("viewer");
        (await db.Hosts.SingleAsync()).MaximumUnfinishedAutomationRuns.ShouldBe(9);
        var flow = new AutomationFlowId(Guid.NewGuid());
        var source = new AutomationNodeId(Guid.NewGuid());
        await AutomationScheduleSkipStore.AppendAsync(
            db,
            new(host),
            flow,
            source,
            AutomationScheduleSkipReason.Disabled,
            new(now.AddSeconds(-1), TimeSpan.Zero),
            new(now, TimeSpan.Zero),
            default
        );
        db.ChangeTracker.Clear();
        var fact = await db.AutomationScheduleSkips.SingleAsync();
        fact.SourceNodeId.ShouldBe(source.Value);
        fact.FlowId.ShouldBe(flow.Value);
        fact.Reason.ShouldBe((int)AutomationScheduleSkipReason.Disabled);
        fact.ExpiresAtUtc.ShouldBe(now.AddHours(24));
    }
}
