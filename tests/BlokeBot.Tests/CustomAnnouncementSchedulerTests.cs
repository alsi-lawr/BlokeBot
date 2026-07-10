using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            CustomAnnouncementScheduleType.Interval,
            ["First", "Second"],
            createdAtUtc: now.AddMinutes(-31).UtcDateTime,
            intervalMinutes: 30
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
            CustomAnnouncementScheduleType.IntervalAfterChat,
            ["After chat"],
            createdAtUtc: now.AddHours(-1).UtcDateTime,
            intervalMinutes: 30,
            requiredChatMessages: 2
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
            CustomAnnouncementScheduleType.Weekly,
            ["Weekly"],
            createdAtUtc: now.AddDays(-7).UtcDateTime,
            weeklyDay: DayOfWeek.Saturday,
            weeklyTime: new TimeOnly(0, 0)
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
            CustomAnnouncementScheduleType.Weekly,
            ["Weekly"],
            createdAtUtc: missedAt.AddDays(-7).UtcDateTime,
            weeklyDay: DayOfWeek.Saturday,
            weeklyTime: new TimeOnly(0, 0)
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
            CustomAnnouncementScheduleType.Interval,
            ["Stopped"],
            createdAtUtc: now.AddHours(-1).UtcDateTime
        );
        await SeedAnnouncementAsync(
            dbFactory,
            disabledHostId,
            CustomAnnouncementScheduleType.Interval,
            ["Disabled"],
            createdAtUtc: now.AddHours(-1).UtcDateTime
        );
        var sender = new RecordingChatMessageSender();
        var scheduler = CreateScheduler(dbFactory, clock, sender);

        await scheduler.RunTickAsync(CancellationToken.None);

        sender.Messages.ShouldBeEmpty();
    }

    private static CustomAnnouncementScheduler CreateScheduler(
        SqliteBlokeBotDbFactory dbFactory,
        TimeProvider clock,
        ITwitchChatMessageSender sender
    )
    {
        var services = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        return new CustomAnnouncementScheduler(
            dbFactory,
            services,
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
            clock,
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
        CustomAnnouncementScheduleType scheduleType,
        string[] variants,
        DateTime createdAtUtc,
        int intervalMinutes = 30,
        int requiredChatMessages = 0,
        DayOfWeek? weeklyDay = null,
        TimeOnly? weeklyTime = null
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

        var announcement = new CustomAnnouncement
        {
            HostId = hostId,
            Name = $"announcement-{Guid.NewGuid():N}",
            Enabled = true,
            MessageLibraryEntryId = entry.Id,
            ScheduleType = scheduleType,
            IntervalMinutes = intervalMinutes,
            RequiredChatMessages = requiredChatMessages,
            WeeklyDay = weeklyDay,
            WeeklyTime = weeklyTime,
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

    private sealed class RecordingChatMessageSender : ITwitchChatMessageSender
    {
        public List<SentChatMessage> Messages { get; } = [];

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
