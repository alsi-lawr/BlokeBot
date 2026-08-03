using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomAnnouncementRecurrenceTests : CustomAnnouncementSchedulerTestBase
{
    [Test]
    public async Task DueIntervalAnnouncement_RunningTicks_SendsOnceAndPersistsRotation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var seed = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["First", "Second"],
            createdAtUtc: now.AddMinutes(-30).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "First")]);
        await using var db = await dbFactory.CreateDbContextAsync();
        var announcement = await db.CustomAnnouncements.SingleAsync(x =>
            x.Id == seed.AnnouncementId
        );
        announcement.LastSentAtUtc.ShouldBe(now.UtcDateTime);
        announcement.ChatMessagesSinceLastSent.ShouldBe(0);
        var currentIndex = await db
            .CustomMessageLibraryEntries.Where(x => x.Id == seed.MessageLibraryEntryId)
            .Select(x => x.CurrentVariantIndex)
            .SingleAsync();
        currentIndex.ShouldBe(1);
    }

    [Test]
    public async Task IntervalAnnouncementWithHistoricalSend_RunningTicks_UsesHistoryThenSendsLaterRecurrence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var lastSentAt = new DateTimeOffset(2026, 7, 10, 11, 50, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(lastSentAt.AddMinutes(29));
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: lastSentAt.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["Interval"],
            createdAtUtc: lastSentAt.AddHours(-6).UtcDateTime,
            lastSentAtUtc: lastSentAt.UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Messages.ShouldBeEmpty();

        clock.SetUtcNow(lastSentAt.AddMinutes(30));
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "Interval")]);
    }

    [Test]
    public async Task IntervalAfterChatBelowAndAtThreshold_RunningTicks_SendsOnlyAtThresholdAndResets()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var seed = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new IntervalAfterChatCustomAnnouncementSchedule
            {
                IntervalMinutes = 30,
                RequiredChatMessages = 2,
            },
            ["After chat"],
            createdAtUtc: now.AddMinutes(-30).UtcDateTime
        );
        var activity = new CustomAnnouncementChatActivity(dbFactory, clock);
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await activity.MessageReceivedAsync(
            Message("viewer", "streamer", "hello"),
            CancellationToken.None
        );
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBeEmpty();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var count = await db
                .CustomAnnouncements.Where(x => x.Id == seed.AnnouncementId)
                .Select(x => x.ChatMessagesSinceLastSent)
                .SingleAsync();
            count.ShouldBe(1);
        }

        await activity.MessageReceivedAsync(
            Message("viewer", "streamer", "!hello"),
            CancellationToken.None
        );
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "After chat")]);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var announcement = await db.CustomAnnouncements.SingleAsync(x =>
                x.Id == seed.AnnouncementId
            );
            announcement.ChatMessagesSinceLastSent.ShouldBe(0);
            announcement.LastSentAtUtc.ShouldBe(now.UtcDateTime);
        }
    }

    [Test]
    public async Task DueWeeklyAnnouncement_RunningRepeatedTick_SendsOnceAtScheduledLocalTime()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 20, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            timeZoneId: "Pacific/Auckland",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new WeeklyCustomAnnouncementSchedule
            {
                Day = DayOfWeek.Saturday,
                Time = new TimeOnly(0, 0),
            },
            ["Weekly"],
            createdAtUtc: now.AddDays(-7).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "Weekly")]);
        await using var db = await dbFactory.CreateDbContextAsync();
        var lastSent = await db
            .CustomAnnouncements.Select(x => x.LastSentAtUtc)
            .SingleAsync(CancellationToken.None);
        lastSent.ShouldBe(new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task WeeklyAnnouncementWithHistoricalSend_RunningTicks_DoesNotReplayAndSendsNextWeek()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var lastSentAt = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(lastSentAt.AddSeconds(20));
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: lastSentAt.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new WeeklyCustomAnnouncementSchedule
            {
                Day = DayOfWeek.Friday,
                Time = new TimeOnly(12, 0),
            },
            ["Weekly"],
            createdAtUtc: lastSentAt.AddDays(-14).UtcDateTime,
            lastSentAtUtc: lastSentAt.UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Messages.ShouldBeEmpty();

        clock.SetUtcNow(lastSentAt.AddDays(7).AddSeconds(20));
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "Weekly")]);
    }

    [Test]
    public async Task WeeklyAnnouncementMissedOffline_RunningCurrentAndNextWindow_DoesNotReplayMissedSend()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var missedAt = new DateTimeOffset(2026, 7, 10, 13, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(missedAt);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            timeZoneId: "Pacific/Auckland",
            changedAtUtc: new DateTime(2026, 7, 10, 12, 30, 0, DateTimeKind.Utc)
        );
        _ = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new WeeklyCustomAnnouncementSchedule
            {
                Day = DayOfWeek.Saturday,
                Time = new TimeOnly(0, 0),
            },
            ["Weekly"],
            createdAtUtc: missedAt.AddDays(-7).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);
        clock.SetUtcNow(new DateTimeOffset(2026, 7, 17, 12, 0, 20, TimeSpan.Zero));
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "Weekly")]);
    }
}
