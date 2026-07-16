using BlokeBot.Announcements;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public abstract class CustomAnnouncementSchedulerTestBase
{
    private protected static CustomAnnouncementScheduler CreateScheduler(
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
            new CustomMessageSelector(),
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

    private protected static async Task<int> SeedHostAsync(
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

    private protected static async Task<AnnouncementSeed> SeedAnnouncementAsync(
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

    private protected static async Task<AnnouncementSeed> SeedAnnouncementWithPolicyAsync(
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
                .Select(
                    (text, index) => new CustomMessageVariant { SortOrder = index, Text = text }
                )
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
                OccurrenceLifetime = new AnnouncementOccurrenceLifetime(occurrenceLifetime),
            },
            LastSentAtUtc = lastSentAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
        db.CustomAnnouncements.Add(announcement);
        await db.SaveChangesAsync();
        return new AnnouncementSeed(announcement.Id, entry.Id);
    }

    private protected static ChatMessage Message(string login, string channel, string text)
    {
        return new(login, channel, text, text, new Dictionary<string, string>());
    }

    private protected sealed record AnnouncementSeed(int AnnouncementId, int MessageLibraryEntryId);

    private protected sealed record SentChatMessage(string Channel, string Message);

    private protected sealed record AnnouncementEnqueueCall(
        string Channel,
        string Message,
        DateTimeOffset ExpiresAt
    );

    private protected sealed class ScriptedAnnouncementSender(
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

    private protected sealed class CancellingAnnouncementSender(
        CancellationTokenSource cancellation
    ) : ICustomAnnouncementSender
    {
        public ValueTask<AnnouncementEnqueueOutcome> EnqueueAsync(
            string channel,
            string message,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken
        )
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<AnnouncementEnqueueOutcome>(cancellationToken);
        }
    }

    private protected sealed class ThrowingChannelAnnouncementSender(string throwingChannel)
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

    private protected sealed class RecordingSchedulerLogger : ILogger<CustomAnnouncementScheduler>
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

    private protected sealed class RecordingChatMessageSender : ICustomAnnouncementSender
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

    private protected sealed class FailingChannelSender(string failingChannel)
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

    private protected sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
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
