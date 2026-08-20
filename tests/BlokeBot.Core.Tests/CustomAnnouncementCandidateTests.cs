using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomAnnouncementCandidateTests : CustomAnnouncementSchedulerTestBase
{
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
        _ = await SeedAnnouncementAsync(
            dbFactory,
            stoppedHostId,
            new IntervalCustomAnnouncementSchedule(),
            ["Stopped"],
            createdAtUtc: now.AddHours(-1).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
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
        var scheduler = CreateScheduler(dbFactory, clock, new DisabledCustomAnnouncementSender());

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
        (
            await db.CustomAnnouncements.FindAsync(first.AnnouncementId)
        )!.LastSentAtUtc.ShouldBeNull();
        (await db.CustomAnnouncements.FindAsync(second.AnnouncementId))!.LastSentAtUtc.ShouldBe(
            now.UtcDateTime
        );
    }

    [Test]
    public async Task WeeklyUtcScheduleAtLocalDstGap_RunningTick_SendsAtStoredUtcTime()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var hostId = await SeedHostAsync(
            dbFactory,
            "streamer",
            timeZoneId: "Europe/London",
            changedAtUtc: now.AddHours(-2).UtcDateTime
        );
        _ = await SeedAnnouncementAsync(
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

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "DST")]);
    }
}
