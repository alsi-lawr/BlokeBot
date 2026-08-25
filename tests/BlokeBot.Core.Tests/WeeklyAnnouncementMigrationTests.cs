using BlokeBot.Announcements;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class WeeklyAnnouncementMigrationTests
{
    private const string _previousMigration = "20260815134407_v0.11.0_AutomationNodeDisplayAliases";

    [Test]
    public async Task LocalWeeklyRow_MigratingOnce_PreservesNextScheduledInstant()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateEmptyAsync();
        var reference = DateTimeOffset.UtcNow;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var futureLocal = TimeZoneInfo.ConvertTime(reference, timeZone).AddDays(3).AddHours(1);
        var localSchedule = new WeeklyAnnouncementSchedule(
            futureLocal.DayOfWeek,
            new TimeOnly(futureLocal.Hour, futureLocal.Minute)
        );
        await using (var before = await database.CreateDbContextAsync())
        {
            await before.GetService<IMigrator>().MigrateAsync(_previousMigration);
            await SeedLocalWeeklyAnnouncementAsync(before, localSchedule);
        }
        var expectedUtc = WeeklyAnnouncementScheduleProjection.ToUtc(
            localSchedule,
            timeZone,
            reference
        );
        var expectedNext = WeeklyAnnouncementScheduleProjection.NextUtcOccurrence(
            expectedUtc,
            reference
        );

        await new BlokeBotDatabaseInitializer(database).InitializeAsync(CancellationToken.None);

        await using var verify = await database.CreateDbContextAsync();
        var stored = await verify
            .CustomAnnouncementSchedules.OfType<WeeklyCustomAnnouncementSchedule>()
            .SingleAsync();
        var migrated = new WeeklyAnnouncementSchedule(stored.Day, stored.Time);
        WeeklyAnnouncementScheduleProjection
            .NextUtcOccurrence(migrated, reference)
            .ShouldBe(expectedNext);
    }

    private static async Task SeedLocalWeeklyAnnouncementAsync(
        BlokeBotDbContext db,
        WeeklyAnnouncementSchedule localSchedule
    )
    {
        var now = DateTime.UtcNow;
        const int HostId = 1;
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO hosts
                (Id, BotRuntimeState, CommandsAliasesConfigured, CreatedAtUtc, DisplayName, Login, TimeZoneId)
            VALUES
                ({HostId}, {BotChannelRuntimeState.Stopped}, {false}, {now}, {"Migration host"}, {"migration-host"}, {"America/New_York"});
            """
        );
        var reply = new CustomMessageLibraryEntry
        {
            Name = "Migration reply",
            SelectionMode = CustomMessageSelectionMode.Sequential,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants = [new() { SortOrder = 0, Text = "Weekly" }],
        };
        var announcement = new CustomAnnouncement
        {
            Name = "Migration weekly",
            Enabled = true,
            MessageLibraryEntry = reply,
            Schedule = new WeeklyCustomAnnouncementSchedule
            {
                Day = localSchedule.Day,
                Time = localSchedule.Time,
            },
            DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
            {
                RetryDelay = new(TimeSpan.FromSeconds(2)),
                OccurrenceLifetime = new(TimeSpan.FromSeconds(30)),
            },
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        reply.HostId = HostId;
        _ = db.CustomMessageLibraryEntries.Add(reply);
        _ = await db.SaveChangesAsync();
        announcement.HostId = HostId;
        announcement.MessageLibraryEntryId = reply.Id;
        announcement.Schedule.HostId = HostId;
        announcement.DeliveryPolicy.HostId = HostId;
        _ = db.CustomAnnouncements.Add(announcement);
        _ = await db.SaveChangesAsync();
    }
}
