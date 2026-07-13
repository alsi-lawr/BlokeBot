using BlokeBot.Announcements;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CustomAnnouncementSchedulerTests
{
    [Test]
    public async Task DueIntervalAnnouncement_RunningTicks_SendsOnceAndPersistsRotation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(dbFactory, "streamer", changedAtUtc: now.AddHours(-1).UtcDateTime);
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
        var announcement = await db.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId);
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
        await SeedAnnouncementAsync(
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
        var hostId = await SeedHostAsync(dbFactory, "streamer", changedAtUtc: now.AddHours(-1).UtcDateTime);
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

        await activity.MessageReceivedAsync(Message("viewer", "streamer", "hello"), CancellationToken.None);
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

        await activity.MessageReceivedAsync(Message("viewer", "streamer", "!hello"), CancellationToken.None);
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "After chat")]);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var announcement = await db.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId);
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
        await SeedAnnouncementAsync(
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
        await SeedAnnouncementAsync(
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
        await SeedAnnouncementAsync(
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

    [Test]
    public async Task StoppedOrFeatureDisabledHost_RunningTick_SendsNothing()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var stoppedHostId = await SeedHostAsync(
            dbFactory,
            "stopped",
            runtimeState: BotChannelRuntimeState.Stopped,
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var disabledHostId = await SeedHostAsync(
            dbFactory,
            "disabled",
            enabledFeatures: HostFeatureFlags.Points,
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        await SeedAnnouncementAsync(
            dbFactory,
            stoppedHostId,
            new IntervalCustomAnnouncementSchedule(),
            ["Stopped"],
            createdAtUtc: now.AddHours(-1).UtcDateTime
        );
        await SeedAnnouncementAsync(
            dbFactory,
            disabledHostId,
            new IntervalCustomAnnouncementSchedule(),
            ["Disabled"],
            createdAtUtc: now.AddHours(-1).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task DueAnnouncementWithDisabledSender_RunningTick_DoesNotAdvanceSchedule()
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
            ["Message"],
            now.AddMinutes(-30).UtcDateTime
        );
        var scheduler = CreateScheduler(
            dbFactory,
            clock,
            new DisabledCustomAnnouncementSender()
        );

        await scheduler.RunTickAsync(CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var announcement = await db.CustomAnnouncements.SingleAsync(x =>
            x.Id == seed.AnnouncementId
        );
        announcement.LastSentAtUtc.ShouldBeNull();
    }

    [Test]
    public async Task DueAnnouncementWithBlankMessage_RunningTick_DoesNotSendOrAdvance()
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
            ["   "],
            now.AddMinutes(-30).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBeEmpty();
        await using var db = await dbFactory.CreateDbContextAsync();
        var announcement = await db.CustomAnnouncements.SingleAsync(x =>
            x.Id == seed.AnnouncementId
        );
        announcement.LastSentAtUtc.ShouldBeNull();
    }

    [Test]
    public async Task OneChannelSendFailure_RunningTick_ContinuesOtherChannels()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var firstHostId = await SeedHostAsync(
            dbFactory,
            "first",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var secondHostId = await SeedHostAsync(
            dbFactory,
            "second",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var first = await SeedAnnouncementAsync(
            dbFactory,
            firstHostId,
            new IntervalCustomAnnouncementSchedule(),
            ["First"],
            now.AddMinutes(-30).UtcDateTime
        );
        var second = await SeedAnnouncementAsync(
            dbFactory,
            secondHostId,
            new IntervalCustomAnnouncementSchedule(),
            ["Second"],
            now.AddMinutes(-30).UtcDateTime
        );
        var sender = new FailingChannelSender("first");
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("second", "Second")]);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.FindAsync(first.AnnouncementId))!
            .LastSentAtUtc.ShouldBeNull();
        (await db.CustomAnnouncements.FindAsync(second.AnnouncementId))!
            .LastSentAtUtc.ShouldBe(now.UtcDateTime);
    }

    [Test]
    public async Task WeeklyAnnouncementInDstGap_RunningTick_SkipsInvalidLocalTime()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            timeZoneId: "Europe/London",
            changedAtUtc: now.AddHours(-2).UtcDateTime
        );
        await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new WeeklyCustomAnnouncementSchedule
            {
                Day = DayOfWeek.Sunday,
                Time = new TimeOnly(1, 30),
            },
            ["DST"],
            now.AddDays(-7).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task DifferentPolicies_SafeTransientThenRestart_RetryAtPersistedOwnDelays()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var firstHostId = await SeedHostAsync(
            dbFactory,
            "first",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var secondHostId = await SeedHostAsync(
            dbFactory,
            "second",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var first = await SeedAnnouncementWithPolicyAsync(
            dbFactory,
            firstHostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["First"],
            now.AddMinutes(-30).UtcDateTime,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10)
        );
        var second = await SeedAnnouncementWithPolicyAsync(
            dbFactory,
            secondHostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["Second"],
            now.AddMinutes(-30).UtcDateTime,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(20)
        );
        var sender = new ScriptedAnnouncementSender(
            new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                new AnnouncementEnqueueFailureType("Busy")
            ),
            new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                new AnnouncementEnqueueFailureType("Busy")
            ),
            new AnnouncementEnqueueOutcome.Accepted(),
            new AnnouncementEnqueueOutcome.Accepted()
        );

        await CreateScheduler(dbFactory, clock, sender).RunTickAsync(CancellationToken.None);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var rows = await db
                .CustomAnnouncements.Where(x => x.Id == first.AnnouncementId || x.Id == second.AnnouncementId)
                .OrderBy(x => x.Id)
                .ToArrayAsync();
            rows[0].OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.RetryScheduled);
            rows[0].OccurrenceNextAttemptAtUtc.ShouldBe(now.AddSeconds(2).UtcDateTime);
            rows[0].OccurrenceExpiresAtUtc.ShouldBe(now.AddSeconds(10).UtcDateTime);
            rows[1].OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.RetryScheduled);
            rows[1].OccurrenceNextAttemptAtUtc.ShouldBe(now.AddSeconds(5).UtcDateTime);
            rows[1].OccurrenceExpiresAtUtc.ShouldBe(now.AddSeconds(20).UtcDateTime);
        }

        clock.SetUtcNow(now.AddSeconds(2));
        await CreateScheduler(dbFactory, clock, sender).RunTickAsync(CancellationToken.None);
        sender.Calls.Count.ShouldBe(3);
        clock.SetUtcNow(now.AddSeconds(5));
        await CreateScheduler(dbFactory, clock, sender).RunTickAsync(CancellationToken.None);
        sender.Calls.Count.ShouldBe(4);
        sender.Calls.Select(x => x.Message).ShouldBe(
            ["First", "Second", "First", "Second"]
        );

        await using var verify = await dbFactory.CreateDbContextAsync();
        var completed = await verify
            .CustomAnnouncements.Where(x => x.Id == first.AnnouncementId || x.Id == second.AnnouncementId)
            .OrderBy(x => x.Id)
            .ToArrayAsync();
        completed.ShouldAllBe(x => x.OccurrenceStatus == AnnouncementOccurrenceStatus.Accepted);
        completed.ShouldAllBe(x => x.OccurrenceAttemptCount == 2);
    }

    [Test]
    public async Task SafeRetryAtExactExpiry_SkipsOccurrenceAndNextRecurrenceRemainsEligible()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var dueAt = now.AddSeconds(-5);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var seed = await SeedAnnouncementWithPolicyAsync(
            dbFactory,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["Message"],
            dueAt.AddMinutes(-30).UtcDateTime,
            TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(10)
        );
        var sender = new ScriptedAnnouncementSender(
            new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                new AnnouncementEnqueueFailureType("Busy")
            ),
            new AnnouncementEnqueueOutcome.Accepted()
        );
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);
        clock.SetUtcNow(now.AddSeconds(4));
        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Calls.Count.ShouldBe(1);
        clock.SetUtcNow(now.AddSeconds(5));
        await scheduler.RunTickAsync(CancellationToken.None);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var skipped = await db.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId);
            skipped.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.SkippedExpired);
            skipped.LastOccurrenceAtUtc.ShouldBe(dueAt.UtcDateTime);
            skipped.Enabled.ShouldBeTrue();
        }

        clock.SetUtcNow(dueAt.AddMinutes(30));
        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Calls.Count.ShouldBe(2);
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId))
            .OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Accepted);
    }

    [Test]
    public async Task AgedOccurrence_EnqueueCarriesOriginalAbsoluteExpiry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 25, TimeSpan.Zero);
        var dueAt = now.AddSeconds(-25);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        await SeedAnnouncementWithPolicyAsync(
            dbFactory,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["Message"],
            dueAt.AddMinutes(-30).UtcDateTime,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(30)
        );
        var sender = new RecordingChatMessageSender();

        await CreateScheduler(dbFactory, clock, sender).RunTickAsync(CancellationToken.None);

        sender.Deadlines.ShouldBe([dueAt.AddSeconds(30)]);
    }

    [Test]
    public async Task NonRetryableOutcomes_CompleteTerminallyAndNeverAttemptAgain()
    {
        var cases = new (AnnouncementEnqueueOutcome Outcome, AnnouncementOccurrenceStatus Status)[]
        {
            (new AnnouncementEnqueueOutcome.Rejected(), AnnouncementOccurrenceStatus.TerminalRejected),
            (
                new AnnouncementEnqueueOutcome.Ambiguous(
                    new AnnouncementEnqueueFailureType("Ambiguous")
                ),
                AnnouncementOccurrenceStatus.TerminalAmbiguous
            ),
            (
                new AnnouncementEnqueueOutcome.Unexpected(
                    new AnnouncementEnqueueFailureType("Unexpected")
                ),
                AnnouncementOccurrenceStatus.TerminalUnexpected
            ),
        };

        foreach (var testCase in cases)
        {
            await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
            var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
            var clock = new ManualTimeProvider(now);
            var hostId = await SeedHostAsync(
                dbFactory,
                $"host-{testCase.Status}",
                changedAtUtc: now.AddHours(-1).UtcDateTime
            );
            var seed = await SeedAnnouncementAsync(
                dbFactory,
                hostId,
                new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
                ["Message"],
                now.AddMinutes(-30).UtcDateTime
            );
            var sender = new ScriptedAnnouncementSender(testCase.Outcome);
            var scheduler = CreateScheduler(dbFactory, clock, sender);

            await scheduler.RunTickAsync(CancellationToken.None);
            clock.SetUtcNow(now.AddSeconds(1));
            await scheduler.RunTickAsync(CancellationToken.None);

            sender.Calls.Count.ShouldBe(1);
            await using var db = await dbFactory.CreateDbContextAsync();
            var announcement = await db.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId);
            announcement.OccurrenceStatus.ShouldBe(testCase.Status);
            announcement.OccurrenceNextAttemptAtUtc.ShouldBeNull();
        }
    }

    [Test]
    public async Task InvalidTimeZoneAndBlankMessage_CompleteExplicitTerminalClassifications()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var invalidHostId = await SeedHostAsync(
            dbFactory,
            "invalid-zone",
            timeZoneId: "Missing/Zone",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var blankHostId = await SeedHostAsync(
            dbFactory,
            "blank-message",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var invalid = await SeedAnnouncementAsync(
            dbFactory,
            invalidHostId,
            new WeeklyCustomAnnouncementSchedule
            {
                Day = now.DayOfWeek,
                Time = TimeOnly.FromDateTime(now.UtcDateTime),
            },
            ["Message"],
            now.AddDays(-7).UtcDateTime
        );
        var blank = await SeedAnnouncementAsync(
            dbFactory,
            blankHostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["   "],
            now.AddMinutes(-30).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();

        await CreateScheduler(dbFactory, clock, sender).RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBeEmpty();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.SingleAsync(x => x.Id == invalid.AnnouncementId))
            .OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.TerminalInvalidTimeZone);
        var blankAnnouncement = await db.CustomAnnouncements.SingleAsync(x => x.Id == blank.AnnouncementId);
        blankAnnouncement.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.TerminalMissingMessage);
        blankAnnouncement.OccurrenceAttemptCount.ShouldBe(0);
    }

    [Test]
    public async Task Cancellation_DuringEnqueueRemainsCancellationAndRestartDoesNotDuplicate()
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
            ["Message"],
            now.AddMinutes(-30).UtcDateTime
        );
        using var cancellation = new CancellationTokenSource();
        var cancellingSender = new CancellingAnnouncementSender(cancellation);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateScheduler(dbFactory, clock, cancellingSender)
                .RunTickAsync(cancellation.Token)
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId))
                .OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Attempting);
        }

        var replacement = new RecordingChatMessageSender();
        await CreateScheduler(dbFactory, clock, replacement)
            .RunTickAsync(CancellationToken.None);
        replacement.Messages.ShouldBeEmpty();
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId))
            .OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.TerminalAmbiguous);
    }

    [Test]
    public async Task UnexpectedCandidateFault_IsReportedRedactedAndOtherCandidateContinues()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var firstHostId = await SeedHostAsync(
            dbFactory,
            "first",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var secondHostId = await SeedHostAsync(
            dbFactory,
            "second",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var first = await SeedAnnouncementAsync(
            dbFactory,
            firstHostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["private first payload"],
            now.AddMinutes(-30).UtcDateTime
        );
        var second = await SeedAnnouncementAsync(
            dbFactory,
            secondHostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["second"],
            now.AddMinutes(-30).UtcDateTime
        );
        var sender = new ThrowingChannelAnnouncementSender("first");
        var logger = new RecordingSchedulerLogger();

        await CreateScheduler(dbFactory, clock, sender, logger)
            .RunTickAsync(CancellationToken.None);

        sender.AcceptedChannels.ShouldBe(["second"]);
        var failure = logger.Entries.Single(x => x.Contains("candidate processing failed"));
        failure.ShouldContain("FailureType: InvalidOperationException");
        failure.ShouldNotContain("private first payload");
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.SingleAsync(x => x.Id == first.AnnouncementId))
            .OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Attempting);
        (await db.CustomAnnouncements.SingleAsync(x => x.Id == second.AnnouncementId))
            .OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Accepted);
    }

    private static CustomAnnouncementScheduler CreateScheduler(
        SqliteBlokeBotDbFactory dbFactory,
        TimeProvider clock,
        ICustomAnnouncementSender sender,
        ILogger<CustomAnnouncementScheduler>? logger = null
    )
    {
        return new CustomAnnouncementScheduler(
            dbFactory,
            sender,
            new TimeProviderCustomAnnouncementTickScheduler(clock),
            new CustomMessageSelector(clock),
            Options.Create(
                new BlokeBotOptions
                {
                    CustomCommands = new BlokeBotCustomCommandOptions
                    {
                        AnnouncementSchedulerTickSeconds = 5,
                    },
                }
            ),
            logger ?? NullLogger<CustomAnnouncementScheduler>.Instance
        );
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login,
        string timeZoneId = "UTC",
        HostFeatureFlags enabledFeatures = HostFeatureFlags.All,
        BotChannelRuntimeState runtimeState = BotChannelRuntimeState.Started,
        DateTime? changedAtUtc = null
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TimeZoneId = timeZoneId,
            EnabledFeatures = enabledFeatures,
            BotRuntimeState = runtimeState,
            BotRuntimeStateChangedAtUtc = changedAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<AnnouncementSeed> SeedAnnouncementAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        CustomAnnouncementSchedule schedule,
        string[] variants,
        DateTime createdAtUtc,
        DateTime? lastSentAtUtc = null
    )
    {
        return await SeedAnnouncementWithPolicyAsync(
            dbFactory,
            hostId,
            schedule,
            variants,
            createdAtUtc,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(30),
            lastSentAtUtc
        );
    }

    private static async Task<AnnouncementSeed> SeedAnnouncementWithPolicyAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        CustomAnnouncementSchedule schedule,
        string[] variants,
        DateTime createdAtUtc,
        TimeSpan retryDelay,
        TimeSpan occurrenceLifetime,
        DateTime? lastSentAtUtc = null
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = new CustomMessageLibraryEntry
        {
            HostId = hostId,
            Name = $"message-{Guid.NewGuid():N}",
            SelectionMode = CustomMessageSelectionMode.Sequential,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            Variants = variants
                .Select((text, index) => new CustomMessageVariant
                {
                    SortOrder = index,
                    Text = text,
                })
                .ToList(),
        };
        db.CustomMessageLibraryEntries.Add(entry);
        await db.SaveChangesAsync();

        schedule.HostId = hostId;

        var announcement = new CustomAnnouncement
        {
            HostId = hostId,
            Name = $"announcement-{Guid.NewGuid():N}",
            Enabled = true,
            MessageLibraryEntryId = entry.Id,
            Schedule = schedule,
            DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
            {
                HostId = hostId,
                RetryDelay = new AnnouncementRetryDelay(retryDelay),
                OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
                    occurrenceLifetime
                ),
            },
            LastSentAtUtc = lastSentAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
        db.CustomAnnouncements.Add(announcement);
        await db.SaveChangesAsync();
        return new AnnouncementSeed(announcement.Id, entry.Id);
    }

    private static TwitchChatMessage Message(string login, string channel, string text)
    {
        return new(login, channel, text, text, new Dictionary<string, string>());
    }

    private sealed record AnnouncementSeed(int AnnouncementId, int MessageLibraryEntryId);

    private sealed record SentChatMessage(string Channel, string Message);

    private sealed record AnnouncementEnqueueCall(
        string Channel,
        string Message,
        DateTimeOffset ExpiresAt
    );

    private sealed class ScriptedAnnouncementSender(
        params AnnouncementEnqueueOutcome[] outcomes
    ) : ICustomAnnouncementSender
    {
        private readonly Queue<AnnouncementEnqueueOutcome> _remaining = new(outcomes);

        public List<AnnouncementEnqueueCall> Calls { get; } = [];

        public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            string channel,
            string message,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new AnnouncementEnqueueCall(channel, message, expiresAt));
            return ValueTask.FromResult(
                _remaining.Count > 0
                    ? _remaining.Dequeue()
                    : throw new InvalidOperationException("No scripted enqueue outcome remains.")
            );
        }
    }

    private sealed class CancellingAnnouncementSender(CancellationTokenSource cancellation)
        : ICustomAnnouncementSender
    {
        public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            string channel,
            string message,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<AnnouncementEnqueueOutcome>(
                cancellationToken
            );
        }
    }

    private sealed class ThrowingChannelAnnouncementSender(string throwingChannel)
        : ICustomAnnouncementSender
    {
        public List<string> AcceptedChannels { get; } = [];

        public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            string channel,
            string message,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken
        )
        {
            if (channel == throwingChannel)
            {
                throw new InvalidOperationException("sensitive provider detail");
            }

            AcceptedChannels.Add(channel);
            return ValueTask.FromResult<AnnouncementEnqueueOutcome>(
                new AnnouncementEnqueueOutcome.Accepted()
            );
        }
    }

    private sealed class RecordingSchedulerLogger : ILogger<CustomAnnouncementScheduler>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(formatter(state, exception));
        }
    }

    private sealed class RecordingChatMessageSender : ICustomAnnouncementSender
    {
        public List<SentChatMessage> Messages { get; } = [];

        public List<DateTimeOffset> Deadlines { get; } = [];

        public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            string channel,
            string message,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(new SentChatMessage(channel, message));
            Deadlines.Add(expiresAt);
            return ValueTask.FromResult<AnnouncementEnqueueOutcome>(
                new AnnouncementEnqueueOutcome.Accepted()
            );
        }
    }

    private sealed class FailingChannelSender(string failingChannel)
        : ICustomAnnouncementSender
    {
        public List<SentChatMessage> Messages { get; } = [];

        public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            string channel,
            string message,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken
        )
        {
            if (channel == failingChannel)
            {
                return ValueTask.FromResult<AnnouncementEnqueueOutcome>(
                    new AnnouncementEnqueueOutcome.Unexpected(
                        new AnnouncementEnqueueFailureType("TestFailure")
                    )
                );
            }

            Messages.Add(new SentChatMessage(channel, message));
            return ValueTask.FromResult<AnnouncementEnqueueOutcome>(
                new AnnouncementEnqueueOutcome.Accepted()
            );
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _current = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _current;
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            _current = value;
        }
    }
}
