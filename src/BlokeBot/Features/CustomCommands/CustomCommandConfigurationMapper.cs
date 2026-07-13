using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

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
        CustomAnnouncementEditor editor
    )
    {
        return new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            HostId = hostId,
            RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(editor.RetryDelaySeconds)),
            OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
                TimeSpan.FromSeconds(editor.OccurrenceLifetimeSeconds)
            ),
        };
    }

    public static void ApplyDeliveryPolicy(
        CustomAnnouncementDeliveryPolicy policy,
        CustomAnnouncementEditor editor
    )
    {
        var retry = RequireRetryUntilExpiredThenSkip(policy);
        retry.RetryDelay = new AnnouncementRetryDelay(
            TimeSpan.FromSeconds(editor.RetryDelaySeconds)
        );
        retry.OccurrenceLifetime = new AnnouncementOccurrenceLifetime(
            TimeSpan.FromSeconds(editor.OccurrenceLifetimeSeconds)
        );
    }

    public static CustomCommandAction CreateAction(
        int hostId,
        ICustomCommandActionEditor editor,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        return editor switch
        {
            MessageCustomCommandActionEditor => new MessageCustomCommandAction
            {
                HostId = hostId,
                MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id,
            },
            CounterCustomCommandActionEditor counter => new CounterCustomCommandAction
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
        ICustomCommandActionEditor editor,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        action.MessageLibraryEntryId = messageEntries[editor.MessageLibraryEntryId].Id;
        if (
            action is CounterCustomCommandAction counterAction
            && editor is CounterCustomCommandActionEditor counterEditor
        )
        {
            counterAction.CounterId = counters[counterEditor.CounterId].Id;
        }
    }

    public static CustomAnnouncementSchedule CreateSchedule(
        int hostId,
        ICustomAnnouncementScheduleEditor editor
    )
    {
        return editor switch
        {
            IntervalCustomAnnouncementScheduleEditor => new IntervalCustomAnnouncementSchedule
            {
                HostId = hostId,
            },
            IntervalAfterChatCustomAnnouncementScheduleEditor =>
                new IntervalAfterChatCustomAnnouncementSchedule { HostId = hostId },
            WeeklyCustomAnnouncementScheduleEditor => new WeeklyCustomAnnouncementSchedule
            {
                HostId = hostId,
            },
            _ => throw new InvalidOperationException("Unsupported custom announcement schedule."),
        };
    }

    public static void ApplySchedule(
        CustomAnnouncementSchedule schedule,
        ICustomAnnouncementScheduleEditor editor
    )
    {
        switch (schedule, editor)
        {
            case (
                IntervalCustomAnnouncementSchedule stored,
                IntervalCustomAnnouncementScheduleEditor edited
            ):
                stored.IntervalMinutes = edited.IntervalMinutes;
                return;
            case (
                IntervalAfterChatCustomAnnouncementSchedule stored,
                IntervalAfterChatCustomAnnouncementScheduleEditor edited
            ):
                stored.IntervalMinutes = edited.IntervalMinutes;
                stored.RequiredChatMessages = edited.RequiredChatMessages;
                return;
            case (
                WeeklyCustomAnnouncementSchedule stored,
                WeeklyCustomAnnouncementScheduleEditor edited
            ):
                stored.Day = edited.Day;
                stored.Time = edited.Time;
                return;
            default:
                throw new InvalidOperationException(
                    "Custom announcement schedule types do not match."
                );
        }
    }

    public static bool ActionMatches(CustomCommandAction action, ICustomCommandActionEditor editor)
    {
        return (action, editor)
            is
                (MessageCustomCommandAction, MessageCustomCommandActionEditor)
                or
                (CounterCustomCommandAction, CounterCustomCommandActionEditor);
    }

    public static bool ScheduleMatches(
        CustomAnnouncementSchedule schedule,
        ICustomAnnouncementScheduleEditor editor
    )
    {
        return (schedule, editor)
            is
                (IntervalCustomAnnouncementSchedule, IntervalCustomAnnouncementScheduleEditor)
                or
                (
                    IntervalAfterChatCustomAnnouncementSchedule,
                    IntervalAfterChatCustomAnnouncementScheduleEditor
                )
                or
                (WeeklyCustomAnnouncementSchedule, WeeklyCustomAnnouncementScheduleEditor);
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
