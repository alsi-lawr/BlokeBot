using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomAnnouncementDeliveryTests : CustomAnnouncementSchedulerTestBase
{
    [Test]
    public async Task SafeRetry_ReusesFirstRenderedRandomMessage()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["{random_from|{random_between|4|4}}"],
            now.AddMinutes(-30).UtcDateTime
        );
        var random = new CountingRandomSource();
        var sender = new ScriptedAnnouncementSender(
            new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                new AnnouncementEnqueueFailureType("Busy")
            ),
            new AnnouncementEnqueueOutcome.Accepted()
        );
        var scheduler = CreateScheduler(dbFactory, clock, sender, random: random);

        await scheduler.RunTickAsync(CancellationToken.None);
        clock.SetUtcNow(now.AddSeconds(2));
        await CreateScheduler(dbFactory, clock, sender, random: random)
            .RunTickAsync(CancellationToken.None);

        sender.Calls.Select(static call => call.Message).ShouldBe(["4", "4"]);
        random.CallCount.ShouldBe(1);
        random.Bounds.ShouldBe([(4, 4)]);
    }

    [Test]
    public async Task NativeRateLimit_RunningTick_PersistsRetryResultAndRetainsSelectedMessage()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var seed = await SeedAnnouncementAsync(
            dbFactory,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            ["Selected reply"],
            now.AddMinutes(-30).UtcDateTime
        );
        await using (var configure = await dbFactory.CreateDbContextAsync())
        {
            var announcement = await configure.CustomAnnouncements.SingleAsync(x =>
                x.Id == seed.AnnouncementId
            );
            announcement.DeliveryType = CustomAnnouncementDeliveryType.TwitchAnnouncement;
            announcement.AnnouncementColor = BlokeBot
                .Persistence
                .Models
                .TwitchAnnouncementColor
                .Green;
            _ = await configure.SaveChangesAsync();
        }
        var sender = new ScriptedAnnouncementSender(
            new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                new AnnouncementEnqueueFailureType("RateLimited"),
                CustomAnnouncementLatestDeliveryResult.RateLimitRetry
            )
        );

        await CreateScheduler(dbFactory, new ManualTimeProvider(now), sender)
            .RunTickAsync(CancellationToken.None);

        sender
            .Calls.Single()
            .DeliveryType.ShouldBe(CustomAnnouncementDeliveryType.TwitchAnnouncement);
        sender
            .Calls.Single()
            .AnnouncementColor.ShouldBe(BlokeBot.Persistence.Models.TwitchAnnouncementColor.Green);
        await using var verify = await dbFactory.CreateDbContextAsync();
        var stored = await verify.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId);
        stored.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.RetryScheduled);
        stored.OccurrenceMessage.ShouldBe("Selected reply");
        stored.LatestDeliveryResult.ShouldBe(CustomAnnouncementLatestDeliveryResult.RateLimitRetry);
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
                .CustomAnnouncements.Where(x =>
                    x.Id == first.AnnouncementId || x.Id == second.AnnouncementId
                )
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
        sender.Calls.Select(x => x.Message).ShouldBe(["First", "Second", "First", "Second"]);

        await using var verify = await dbFactory.CreateDbContextAsync();
        var completed = await verify
            .CustomAnnouncements.Where(x =>
                x.Id == first.AnnouncementId || x.Id == second.AnnouncementId
            )
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
            var skipped = await db.CustomAnnouncements.SingleAsync(x =>
                x.Id == seed.AnnouncementId
            );
            skipped.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.SkippedExpired);
            skipped.LastOccurrenceAtUtc.ShouldBe(dueAt.UtcDateTime);
            skipped.Enabled.ShouldBeTrue();
        }

        clock.SetUtcNow(dueAt.AddMinutes(30));
        await scheduler.RunTickAsync(CancellationToken.None);
        sender.Calls.Count.ShouldBe(2);
        await using var verify = await dbFactory.CreateDbContextAsync();
        (
            await verify.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId)
        ).OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Accepted);
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
        _ = await SeedAnnouncementWithPolicyAsync(
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
            (
                new AnnouncementEnqueueOutcome.Rejected(),
                AnnouncementOccurrenceStatus.TerminalRejected
            ),
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
            var announcement = await db.CustomAnnouncements.SingleAsync(x =>
                x.Id == seed.AnnouncementId
            );
            announcement.OccurrenceStatus.ShouldBe(testCase.Status);
            announcement.OccurrenceNextAttemptAtUtc.ShouldBeNull();
        }
    }

    [Test]
    public async Task InvalidDisplayTimeZoneAndBlankMessage_UseUtcScheduleAndClassifyBlankMessage()
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

        sender.Messages.ShouldBe([new SentChatMessage("invalid-zone", "Message")]);
        await using var db = await dbFactory.CreateDbContextAsync();
        (
            await db.CustomAnnouncements.SingleAsync(x => x.Id == invalid.AnnouncementId)
        ).OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Accepted);
        var blankAnnouncement = await db.CustomAnnouncements.SingleAsync(x =>
            x.Id == blank.AnnouncementId
        );
        blankAnnouncement.OccurrenceStatus.ShouldBe(
            AnnouncementOccurrenceStatus.TerminalMissingMessage
        );
        blankAnnouncement.OccurrenceAttemptCount.ShouldBe(0);
    }

    [Test]
    public async Task ScheduledNestedNumericFailure_PersistsReplacementBeforeDeliveryAndReusesItOnRetry()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            database,
            "streamer",
            changedAtUtc: now.AddHours(-1).UtcDateTime
        );
        var seed = await SeedAnnouncementAsync(
            database,
            hostId,
            new IntervalCustomAnnouncementSchedule { IntervalMinutes = 30 },
            [
                "private-prefix {random_between|1|1} {random_from|{random_between|1|{arg1}}} private-suffix",
            ],
            now.AddMinutes(-30).UtcDateTime
        );
        var random = new CountingRandomSource();
        var sender = new PersistedPayloadSender(database, seed.AnnouncementId);

        await CreateScheduler(database, clock, sender, random: random)
            .RunTickAsync(CancellationToken.None);
        await using (var pending = await database.CreateDbContextAsync())
        {
            var occurrence = await pending.CustomAnnouncements.SingleAsync();
            occurrence.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.RetryScheduled);
            occurrence.OccurrenceMessage.ShouldBe(sender.Messages.Single());
        }
        clock.SetUtcNow(now.AddSeconds(2));
        await CreateScheduler(database, clock, sender, random: random)
            .RunTickAsync(CancellationToken.None);

        sender.Messages.Count.ShouldBe(2);
        sender.Messages[0].ShouldContain("whole");
        sender.Messages[0].ShouldNotContain("private-");
        sender.Messages[0].ShouldNotContain("{arg1}");
        sender.Messages[1].ShouldBe(sender.Messages[0]);
        random.Bounds.ShouldBe([(1, 1)]);
        random.CallCount.ShouldBe(1);
        await using var verify = await database.CreateDbContextAsync();
        var stored = await verify.CustomAnnouncements.SingleAsync();
        stored.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Accepted);
        stored.OccurrenceAttemptCount.ShouldBe(2);
        stored.OccurrenceMessage.ShouldBeNull();
    }

    private sealed class PersistedPayloadSender(
        SqliteBlokeBotDbFactory database,
        int announcementId
    ) : ICustomAnnouncementSender
    {
        public List<string> Messages { get; } = [];

        public async ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            CustomAnnouncementDeliveryRequest request,
            CancellationToken cancellationToken
        )
        {
            await using var verify = await database.CreateDbContextAsync(cancellationToken);
            var stored = await verify.CustomAnnouncements.SingleAsync(
                value => value.Id == announcementId,
                cancellationToken
            );
            stored.OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Attempting);
            stored.OccurrenceMessage.ShouldBe(request.Message);
            Messages.Add(request.Message);
            return Messages.Count == 1
                ? new AnnouncementEnqueueOutcome.SafePreEnqueueTransient(
                    new AnnouncementEnqueueFailureType("Busy")
                )
                : new AnnouncementEnqueueOutcome.Accepted();
        }
    }

    private sealed class CountingRandomSource : IMessageLibraryRandomSource
    {
        public int CallCount { get; private set; }

        public int Next(int exclusiveMaximum)
        {
            CallCount++;
            return CallCount - 1;
        }

        public List<(int Minimum, int Maximum)> Bounds { get; } = [];

        public int NextInclusive(int minimum, int maximum)
        {
            Bounds.Add((minimum, maximum));
            return minimum;
        }
    }
}
