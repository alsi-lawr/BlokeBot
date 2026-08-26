using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class DurableAlertRecurrenceMigrationTests
{
    private const string _releasedMigration = "20260822192152_v0.12.0_GuessingSharedAliases";

    [Test]
    public async Task Upgrade_PreservesExistingAlertOccurrenceTime()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync(
            new WeeklyAnnouncementMigrationInterceptor()
        );
        var createdAtUtc = new DateTime(2026, 8, 22, 12, 30, 0, DateTimeKind.Utc);
        await using (var before = await database.CreateDbContextAsync())
        {
            await before.Database.MigrateAsync(_releasedMigration);
            var host = new BotHost
            {
                TwitchUserId = "migration-host-id",
                Login = "migration-host",
                DisplayName = "Migration host",
                CreatedAtUtc = createdAtUtc,
            };
            _ = before.Hosts.Add(host);
            _ = await before.SaveChangesAsync();
            _ = await before.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO durable_alerts (
                    HostId,
                    Severity,
                    Source,
                    SourceKey,
                    Title,
                    Message,
                    LinkPath,
                    CreatedAtUtc,
                    AcknowledgedAtUtc,
                    AcknowledgedByLogin
                ) VALUES (
                    {host.Id},
                    {"Warning"},
                    {"migration"},
                    {"existing-alert"},
                    {"Existing alert"},
                    {"Preserve the released occurrence time."},
                    NULL,
                    {createdAtUtc},
                    NULL,
                    NULL
                );
                """
            );
        }

        await using (var migrate = await database.CreateDbContextAsync())
        {
            await migrate.Database.MigrateAsync();
        }

        await using var verify = await database.CreateDbContextAsync();
        var alert = await verify.DurableAlerts.AsNoTracking().SingleAsync();
        alert.CreatedAtUtc.ShouldBe(createdAtUtc);
        alert.LastOccurredAtUtc.ShouldBe(createdAtUtc);
        alert.OccurrenceCount.ShouldBe(1);
    }
}
