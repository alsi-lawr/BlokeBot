using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomCommandConfigurationMapper
{
    public static CustomMessageLibraryEntryEditor ToEditor(CustomMessageLibraryEntry entry)
    {
        return new()
        {
            Id = entry.Id,
            Name = entry.Name,
            SelectionMode = entry.SelectionMode,
            CurrentVariantIndex = entry.CurrentVariantIndex,
            Variants = entry
                .Variants.OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new CustomMessageVariantEditor { Id = x.Id, Text = x.Text })
                .ToList(),
        };
    }

    public static CustomCounterEditor ToEditor(CustomCounter counter)
    {
        return new()
        {
            Id = counter.Id,
            Name = counter.Name,
            Value = counter.Value,
        };
    }

    public static CustomCommandEditor ToEditor(CustomCommand command)
    {
        return new()
        {
            Id = command.Id,
            Name = command.Name,
            Aliases = string.Join(", ", command.Aliases.Select(x => x.Alias).Order()),
            Enabled = command.Enabled,
            ModeratorOnly = command.ModeratorOnly,
            CooldownSeconds = command.CooldownSeconds,
            CooldownScope = command.CooldownScope,
            Action = command.Action switch
            {
                MessageCustomCommandAction action => new MessageCustomCommandActionEditor
                {
                    MessageLibraryEntryId = action.MessageLibraryEntryId,
                },
                CounterCustomCommandAction action => new CounterCustomCommandActionEditor
                {
                    MessageLibraryEntryId = action.MessageLibraryEntryId,
                    CounterId = action.CounterId,
                },
                _ => throw new InvalidOperationException("Unsupported custom command action."),
            },
        };
    }

    public static CustomAnnouncementEditor ToEditor(CustomAnnouncement announcement)
    {
        return new()
        {
            Id = announcement.Id,
            Name = announcement.Name,
            Enabled = announcement.Enabled,
            MessageLibraryEntryId = announcement.MessageLibraryEntryId,
            RetryDelaySeconds = ToWholeSeconds(
                RequireRetryUntilExpiredThenSkip(announcement.DeliveryPolicy).RetryDelay.Value
            ),
            OccurrenceLifetimeSeconds = ToWholeSeconds(
                RequireRetryUntilExpiredThenSkip(
                    announcement.DeliveryPolicy
                ).OccurrenceLifetime.Value
            ),
            Schedule = announcement.Schedule switch
            {
                IntervalCustomAnnouncementSchedule schedule =>
                    new IntervalCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = schedule.IntervalMinutes,
                    },
                IntervalAfterChatCustomAnnouncementSchedule schedule =>
                    new IntervalAfterChatCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = schedule.IntervalMinutes,
                        RequiredChatMessages = schedule.RequiredChatMessages,
                    },
                WeeklyCustomAnnouncementSchedule schedule =>
                    new WeeklyCustomAnnouncementScheduleEditor
                    {
                        Day = schedule.Day,
                        Time = schedule.Time,
                    },
                _ => throw new InvalidOperationException(
                    "Unsupported custom announcement schedule."
                ),
            },
            LastSentAtUtc = announcement.LastSentAtUtc,
            ChatMessagesSinceLastSent = announcement.ChatMessagesSinceLastSent,
        };
    }

    public static CustomAnnouncementDeliveryPolicy CreateDeliveryPolicy(
        int hostId,
        CustomAnnouncementValue announcement
    )
    {
        return new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            HostId = hostId,
            RetryDelay = announcement.RetryDelay,
            OccurrenceLifetime = announcement.OccurrenceLifetime,
        };
    }

    public static void ApplyDeliveryPolicy(
        CustomAnnouncementDeliveryPolicy policy,
        CustomAnnouncementValue announcement
    )
    {
        var retry = RequireRetryUntilExpiredThenSkip(policy);
        retry.RetryDelay = announcement.RetryDelay;
        retry.OccurrenceLifetime = announcement.OccurrenceLifetime;
    }

    public static CustomCommandAction CreateAction(
        int hostId,
        CustomCommandActionValue action,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        return action switch
        {
            CustomCommandActionValue.Message message => new MessageCustomCommandAction
            {
                HostId = hostId,
                MessageLibraryEntryId = messageEntries[message.MessageLibraryEntryId].Id,
            },
            CustomCommandActionValue.Counter counter => new CounterCustomCommandAction
            {
                HostId = hostId,
                MessageLibraryEntryId = messageEntries[counter.MessageLibraryEntryId].Id,
                CounterId = counters[counter.CounterId].Id,
            },
            _ => throw new InvalidOperationException("Unsupported custom command action."),
        };
    }

    public static void ApplyAction(
        CustomCommandAction action,
        CustomCommandActionValue value,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        action.MessageLibraryEntryId = messageEntries[value.MessageLibraryEntryId].Id;
        if (
            action is CounterCustomCommandAction counterAction
            && value is CustomCommandActionValue.Counter counterValue
        )
        {
            counterAction.CounterId = counters[counterValue.CounterId].Id;
        }
    }

    public static CustomAnnouncementSchedule CreateSchedule(
        int hostId,
        CustomAnnouncementScheduleValue schedule
    )
    {
        return schedule switch
        {
            CustomAnnouncementScheduleValue.Interval => new IntervalCustomAnnouncementSchedule
            {
                HostId = hostId,
            },
            CustomAnnouncementScheduleValue.IntervalAfterChat =>
                new IntervalAfterChatCustomAnnouncementSchedule { HostId = hostId },
            CustomAnnouncementScheduleValue.Weekly => new WeeklyCustomAnnouncementSchedule
            {
                HostId = hostId,
            },
            _ => throw new InvalidOperationException("Unsupported custom announcement schedule."),
        };
    }

    public static void ApplySchedule(
        CustomAnnouncementSchedule schedule,
        CustomAnnouncementScheduleValue value
    )
    {
        switch (schedule, value)
        {
            case (
                IntervalCustomAnnouncementSchedule stored,
                CustomAnnouncementScheduleValue.Interval configured
            ):
                stored.IntervalMinutes = configured.IntervalMinutes;
                return;
            case (
                IntervalAfterChatCustomAnnouncementSchedule stored,
                CustomAnnouncementScheduleValue.IntervalAfterChat configured
            ):
                stored.IntervalMinutes = configured.IntervalMinutes;
                stored.RequiredChatMessages = configured.RequiredChatMessages;
                return;
            case (
                WeeklyCustomAnnouncementSchedule stored,
                CustomAnnouncementScheduleValue.Weekly configured
            ):
                stored.Day = configured.Day;
                stored.Time = configured.Time;
                return;
            default:
                throw new InvalidOperationException(
                    "Custom announcement schedule types do not match."
                );
        }
    }

    public static bool ActionMatches(CustomCommandAction action, CustomCommandActionValue value)
    {
        return (action, value)
            is
                (MessageCustomCommandAction, CustomCommandActionValue.Message)
                or
                (CounterCustomCommandAction, CustomCommandActionValue.Counter);
    }

    public static bool ScheduleMatches(
        CustomAnnouncementSchedule schedule,
        CustomAnnouncementScheduleValue value
    )
    {
        return (schedule, value)
            is
                (IntervalCustomAnnouncementSchedule, CustomAnnouncementScheduleValue.Interval)
                or
                (
                    IntervalAfterChatCustomAnnouncementSchedule,
                    CustomAnnouncementScheduleValue.IntervalAfterChat
                )
                or
                (WeeklyCustomAnnouncementSchedule, CustomAnnouncementScheduleValue.Weekly);
    }

    private static RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy RequireRetryUntilExpiredThenSkip(
        CustomAnnouncementDeliveryPolicy policy
    )
    {
        return policy as RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
            ?? throw new UnreachableException("Unknown custom announcement delivery policy.");
    }

    private static int ToWholeSeconds(TimeSpan value)
    {
        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new InvalidOperationException(
                "Announcement delivery timing must use whole seconds."
            );
        }

        return checked((int)(value.Ticks / TimeSpan.TicksPerSecond));
    }
}
