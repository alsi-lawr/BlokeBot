using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomAnnouncementResilienceTests : CustomAnnouncementSchedulerTestBase
{
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

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateScheduler(dbFactory, clock, cancellingSender).RunTickAsync(cancellation.Token)
        );
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (
                await db.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId)
            ).OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Attempting);
        }

        var replacement = new RecordingChatMessageSender();
        await CreateScheduler(dbFactory, clock, replacement).RunTickAsync(CancellationToken.None);
        replacement.Messages.ShouldBeEmpty();
        await using var verify = await dbFactory.CreateDbContextAsync();
        (
            await verify.CustomAnnouncements.SingleAsync(x => x.Id == seed.AnnouncementId)
        ).OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.TerminalAmbiguous);
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
        (
            await db.CustomAnnouncements.SingleAsync(x => x.Id == first.AnnouncementId)
        ).OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Attempting);
        (
            await db.CustomAnnouncements.SingleAsync(x => x.Id == second.AnnouncementId)
        ).OccurrenceStatus.ShouldBe(AnnouncementOccurrenceStatus.Accepted);
    }
}
