using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomCommandConfigurationMapper
{
    public static CustomMessageLibraryEntryEditor ToEditor(CustomMessageLibraryEntry entry) =>
        new()
        {
            Id = entry.Id,
            Name = entry.Name,
            SelectionMode = entry.SelectionMode,
            CurrentVariantIndex = entry.CurrentVariantIndex,
            Variants = entry
                .Variants.OrderBy(static x => x.SortOrder)
                .ThenBy(static x => x.Id)
                .Select(static x => new CustomMessageVariantEditor { Id = x.Id, Text = x.Text })
                .ToList(),
        };

    public static CustomCounterEditor ToEditor(CustomCounter counter) =>
        new()
        {
            Id = counter.Id,
            Name = counter.Name,
            Value = counter.Value,
        };

    public static CustomCommandEditor ToEditor(CustomCommand command) =>
        new()
        {
            Id = command.Id,
            Name = command.Name,
            Aliases = string.Join(
                ", ",
                command
                    .Aliases.OrderBy(static x => x.SortOrder)
                    .ThenBy(static x => x.Id)
                    .Select(static x => x.Alias)
            ),
            Enabled = command.Enabled,
            ModeratorOnly = command.ModeratorOnly,
            CooldownSeconds = command.CooldownSeconds,
            CooldownScope = command.CooldownScope,
            InvocationLimit = command.InvocationLimit,
            Action = command.Action switch
            {
                MessageCustomCommandAction action => new MessageCustomCommandActionEditor
                {
                    ReplyRoutes = ToReplyRoutesEditor(action),
                },
                CounterCustomCommandAction action => new CounterCustomCommandActionEditor
                {
                    ReplyRoutes = ToReplyRoutesEditor(action),
                    CounterId = action.CounterId,
                },
                OverlayCueCustomCommandAction action => new OverlayCueCustomCommandActionEditor
                {
                    ReplyRoutes = ToReplyRoutesEditor(action),
                    TargetOverlayPublicId = action.TargetOverlayPublicId,
                    CuePublicId = action.CuePublicId,
                    QueuePolicy = action.QueuePolicy,
                    ReplyOrder = action.ReplyOrder,
                },
                _ => throw new InvalidOperationException("Unsupported custom command action."),
            },
        };

    public static CustomAnnouncementEditor ToEditor(CustomAnnouncement announcement) =>
        new()
        {
            Id = announcement.Id,
            Name = announcement.Name,
            Enabled = announcement.Enabled,
            MessageLibraryEntryId = announcement.MessageLibraryEntryId,
            DeliveryType = announcement.DeliveryType,
            AnnouncementColor = announcement.AnnouncementColor,
            LatestDeliveryResult = announcement.LatestDeliveryResult,
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

    public static CustomAnnouncementDeliveryPolicy CreateDeliveryPolicy(
        int hostId,
        CustomAnnouncementValue announcement
    ) =>
        new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            HostId = hostId,
            RetryDelay = announcement.RetryDelay,
            OccurrenceLifetime = announcement.OccurrenceLifetime,
        };

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
        CustomCommandAction created = action switch
        {
            CustomCommandActionValue.Message => new MessageCustomCommandAction { HostId = hostId },
            CustomCommandActionValue.Counter counter => new CounterCustomCommandAction
            {
                HostId = hostId,
                CounterId = counters[counter.CounterId].Id,
            },
            CustomCommandActionValue.OverlayCue cue => new OverlayCueCustomCommandAction
            {
                HostId = hostId,
                TargetOverlayPublicId = cue.TargetOverlayPublicId,
                CuePublicId = cue.CuePublicId,
                QueuePolicy = cue.QueuePolicy,
                ReplyOrder = cue.ReplyOrder,
            },
            _ => throw new InvalidOperationException("Unsupported custom command action."),
        };
        ApplyReplyRoutes(created, action.ReplyRoutes, messageEntries);
        return created;
    }

    public static void ApplyAction(
        CustomCommandAction action,
        CustomCommandActionValue value,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    )
    {
        ApplyReplyRoutes(action, value.ReplyRoutes, messageEntries);
        if (
            action is CounterCustomCommandAction counterAction
            && value is CustomCommandActionValue.Counter counterValue
        )
        {
            counterAction.CounterId = counters[counterValue.CounterId].Id;
        }
        else if (
            action is OverlayCueCustomCommandAction cueAction
            && value is CustomCommandActionValue.OverlayCue cueValue
        )
        {
            cueAction.TargetOverlayPublicId = cueValue.TargetOverlayPublicId;
            cueAction.CuePublicId = cueValue.CuePublicId;
            cueAction.QueuePolicy = cueValue.QueuePolicy;
            cueAction.ReplyOrder = cueValue.ReplyOrder;
        }
    }

    private static CustomCommandReplyRoutesEditor ToReplyRoutesEditor(CustomCommandAction action) =>
        new()
        {
            ZeroArgumentMessageLibraryEntryId = action.ZeroArgumentMessageLibraryEntryId,
            OneArgumentMessageLibraryEntryId = action.OneArgumentMessageLibraryEntryId,
            TwoArgumentMessageLibraryEntryId = action.TwoArgumentMessageLibraryEntryId,
        };

    private static void ApplyReplyRoutes(
        CustomCommandAction action,
        CustomCommandReplyRoutes routes,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries
    )
    {
        action.ZeroArgumentMessageLibraryEntryId = StoredMessageEntryId(
            routes.ZeroArgumentMessageLibraryEntryId,
            messageEntries
        );
        action.OneArgumentMessageLibraryEntryId = StoredMessageEntryId(
            routes.OneArgumentMessageLibraryEntryId,
            messageEntries
        );
        action.TwoArgumentMessageLibraryEntryId = StoredMessageEntryId(
            routes.TwoArgumentMessageLibraryEntryId,
            messageEntries
        );
    }

    private static int? StoredMessageEntryId(
        int? editorId,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries
    ) => editorId is { } id ? messageEntries[id].Id : null;

    public static CustomAnnouncementSchedule CreateSchedule(
        int hostId,
        CustomAnnouncementScheduleValue schedule
    ) =>
        schedule switch
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

    public static bool ActionMatches(CustomCommandAction action, CustomCommandActionValue value) =>
        (action, value)
            is
                (MessageCustomCommandAction, CustomCommandActionValue.Message)
                or
                (CounterCustomCommandAction, CustomCommandActionValue.Counter)
                or
                (OverlayCueCustomCommandAction, CustomCommandActionValue.OverlayCue);

    public static bool ScheduleMatches(
        CustomAnnouncementSchedule schedule,
        CustomAnnouncementScheduleValue value
    ) =>
        (schedule, value)
            is
                (IntervalCustomAnnouncementSchedule, CustomAnnouncementScheduleValue.Interval)
                or
                (
                    IntervalAfterChatCustomAnnouncementSchedule,
                    CustomAnnouncementScheduleValue.IntervalAfterChat
                )
                or
                (WeeklyCustomAnnouncementSchedule, CustomAnnouncementScheduleValue.Weekly);

    private static RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy RequireRetryUntilExpiredThenSkip(
        CustomAnnouncementDeliveryPolicy policy
    ) =>
        policy as RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        ?? throw new UnreachableException("Unknown custom announcement delivery policy.");

    private static int ToWholeSeconds(TimeSpan value) =>
        value.Ticks % TimeSpan.TicksPerSecond != 0
            ? throw new InvalidOperationException(
                "Announcement delivery timing must use whole seconds."
            )
            : checked((int)(value.Ticks / TimeSpan.TicksPerSecond));
}
