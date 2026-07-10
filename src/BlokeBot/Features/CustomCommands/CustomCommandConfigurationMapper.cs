using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

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
                .Variants.OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => new CustomMessageVariantEditor { Id = x.Id, Text = x.Text })
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

    public static CustomAnnouncementEditor ToEditor(CustomAnnouncement announcement) =>
        new()
        {
            Id = announcement.Id,
            Name = announcement.Name,
            Enabled = announcement.Enabled,
            MessageLibraryEntryId = announcement.MessageLibraryEntryId,
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

    public static CustomCommandAction CreateAction(
        int hostId,
        ICustomCommandActionEditor editor,
        IReadOnlyDictionary<int, CustomMessageLibraryEntry> messageEntries,
        IReadOnlyDictionary<int, CustomCounter> counters
    ) =>
        editor switch
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
    ) =>
        editor switch
        {
            IntervalCustomAnnouncementScheduleEditor =>
                new IntervalCustomAnnouncementSchedule { HostId = hostId },
            IntervalAfterChatCustomAnnouncementScheduleEditor =>
                new IntervalAfterChatCustomAnnouncementSchedule { HostId = hostId },
            WeeklyCustomAnnouncementScheduleEditor =>
                new WeeklyCustomAnnouncementSchedule { HostId = hostId },
            _ => throw new InvalidOperationException("Unsupported custom announcement schedule."),
        };

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
                throw new InvalidOperationException("Custom announcement schedule types do not match.");
        }
    }

    public static bool ActionMatches(
        CustomCommandAction action,
        ICustomCommandActionEditor editor
    ) =>
        (action, editor) is
            (MessageCustomCommandAction, MessageCustomCommandActionEditor)
                or (CounterCustomCommandAction, CounterCustomCommandActionEditor);

    public static bool ScheduleMatches(
        CustomAnnouncementSchedule schedule,
        ICustomAnnouncementScheduleEditor editor
    ) =>
        (schedule, editor) is
            (IntervalCustomAnnouncementSchedule, IntervalCustomAnnouncementScheduleEditor)
                or (
                    IntervalAfterChatCustomAnnouncementSchedule,
                    IntervalAfterChatCustomAnnouncementScheduleEditor
                )
                or (WeeklyCustomAnnouncementSchedule, WeeklyCustomAnnouncementScheduleEditor);
}
