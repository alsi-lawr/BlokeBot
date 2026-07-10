using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CustomAnnouncementSchedulerTests
{
    [Test]
    public async Task Interval_announcement_sends_and_persists_last_sent_and_rotation()
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
            createdAtUtc: now.AddMinutes(-31).UtcDateTime
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
    public async Task Interval_after_chat_requires_chat_count_and_resets_after_send()
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
            createdAtUtc: now.AddHours(-1).UtcDateTime
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
    public async Task Weekly_announcement_sends_once_at_scheduled_local_time()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 10, 12, 5, 0, TimeSpan.Zero);
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
    public async Task Weekly_announcement_missed_while_offline_is_not_replayed()
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
        clock.SetUtcNow(new DateTimeOffset(2026, 7, 17, 12, 5, 0, TimeSpan.Zero));
        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBe([new SentChatMessage("streamer", "Weekly")]);
    }

    [Test]
    public async Task Scheduler_ignores_hosts_without_custom_commands_or_started_runtime()
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
    public async Task Due_announcement_is_not_advanced_when_sender_is_disabled()
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
            now.AddHours(-1).UtcDateTime
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
    public async Task Blank_announcement_message_is_not_sent_or_advanced()
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
            now.AddHours(-1).UtcDateTime
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
    public async Task Sender_failure_does_not_block_other_channels()
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
            now.AddHours(-1).UtcDateTime
        );
        var second = await SeedAnnouncementAsync(
            dbFactory,
            secondHostId,
            new IntervalCustomAnnouncementSchedule(),
            ["Second"],
            now.AddHours(-1).UtcDateTime
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
    public async Task Weekly_announcement_skips_invalid_dst_local_time()
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

    private static CustomAnnouncementScheduler CreateScheduler(
        SqliteBlokeBotDbFactory dbFactory,
        TimeProvider clock,
        ICustomAnnouncementSender sender
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
            NullLogger<CustomAnnouncementScheduler>.Instance
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
        DateTime createdAtUtc
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
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
        db.CustomAnnouncements.Add(announcement);
        await db.SaveChangesAsync();
        return new AnnouncementSeed(announcement.Id, entry.Id);
    }

    private static TwitchChatMessage Message(string login, string channel, string text) =>
        new(login, channel, text, text, new Dictionary<string, string>());

    private sealed record AnnouncementSeed(int AnnouncementId, int MessageLibraryEntryId);

    private sealed record SentChatMessage(string Channel, string Message);

    private sealed class RecordingChatMessageSender : ICustomAnnouncementSender
    {
        public List<SentChatMessage> Messages { get; } = [];

        public bool IsEnabled => true;

        public Task SendAsync(
            string channel,
            string message,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(new SentChatMessage(channel, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingChannelSender(string failingChannel)
        : ICustomAnnouncementSender
    {
        public List<SentChatMessage> Messages { get; } = [];

        public bool IsEnabled => true;

        public Task SendAsync(
            string channel,
            string message,
            CancellationToken cancellationToken
        )
        {
            if (channel == failingChannel)
                throw new InvalidOperationException("Send failed.");

            Messages.Add(new SentChatMessage(channel, message));
            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void SetUtcNow(DateTimeOffset value)
        {
            current = value;
        }
    }
}
