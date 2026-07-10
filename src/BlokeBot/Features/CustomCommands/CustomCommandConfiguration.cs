using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandConfiguration
{
    public string TimeZoneId { get; set; } = "UTC";

    public List<CustomMessageLibraryEntryEditor> MessageEntries { get; set; } = [];

    public List<CustomCommandEditor> Commands { get; set; } = [];

    public List<CustomCounterEditor> Counters { get; set; } = [];

    public List<CustomAnnouncementEditor> Announcements { get; set; } = [];

    public CustomCommandAlertSummary AlertSummary { get; set; } = new();
}

public sealed class CustomMessageLibraryEntryEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public CustomMessageSelectionMode SelectionMode { get; set; } =
        CustomMessageSelectionMode.Sequential;

    public int CurrentVariantIndex { get; set; }

    public List<CustomMessageVariantEditor> Variants { get; set; } = [];
}

public sealed class CustomMessageVariantEditor
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class CustomCommandEditor
{
    private ICustomCommandActionEditor action = new MessageCustomCommandActionEditor();

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Aliases { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool ModeratorOnly { get; set; }

    public int CooldownSeconds { get; set; }

    public CustomCommandCooldownScope CooldownScope { get; set; } =
        CustomCommandCooldownScope.Global;

    public ICustomCommandActionEditor Action
    {
        get => action;
        set => action = value ?? throw new ArgumentNullException(nameof(value));
    }

    public CustomCommandActionKind ActionKind
    {
        get => Action.Kind;
        set
        {
            if (value == Action.Kind)
                return;

            Action = value switch
            {
                CustomCommandActionKind.Message => new MessageCustomCommandActionEditor
                {
                    MessageLibraryEntryId = Action.MessageLibraryEntryId,
                },
                CustomCommandActionKind.Counter => new CounterCustomCommandActionEditor
                {
                    MessageLibraryEntryId = Action.MessageLibraryEntryId,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }
    }
}

public enum CustomCommandActionKind
{
    Message,
    Counter,
}

public interface ICustomCommandActionEditor
{
    CustomCommandActionKind Kind { get; }

    int MessageLibraryEntryId { get; set; }
}

public sealed class MessageCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Message;

    public int MessageLibraryEntryId { get; set; }
}

public sealed class CounterCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Counter;

    public int MessageLibraryEntryId { get; set; }

    public int CounterId { get; set; }
}

public sealed class CustomCounterEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Value { get; set; }
}

public sealed class CustomAnnouncementEditor
{
    private ICustomAnnouncementScheduleEditor schedule =
        new IntervalCustomAnnouncementScheduleEditor();

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int MessageLibraryEntryId { get; set; }

    public ICustomAnnouncementScheduleEditor Schedule
    {
        get => schedule;
        set => schedule = value ?? throw new ArgumentNullException(nameof(value));
    }

    public CustomAnnouncementScheduleKind ScheduleKind
    {
        get => Schedule.Kind;
        set
        {
            if (value == Schedule.Kind)
                return;

            var intervalMinutes = Schedule switch
            {
                IntervalCustomAnnouncementScheduleEditor interval =>
                    interval.IntervalMinutes,
                IntervalAfterChatCustomAnnouncementScheduleEditor intervalAfterChat =>
                    intervalAfterChat.IntervalMinutes,
                _ => 30,
            };
            Schedule = value switch
            {
                CustomAnnouncementScheduleKind.Interval =>
                    new IntervalCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = intervalMinutes,
                    },
                CustomAnnouncementScheduleKind.IntervalAfterChat =>
                    new IntervalAfterChatCustomAnnouncementScheduleEditor
                    {
                        IntervalMinutes = intervalMinutes,
                    },
                CustomAnnouncementScheduleKind.Weekly =>
                    new WeeklyCustomAnnouncementScheduleEditor
                    {
                        Day = DayOfWeek.Monday,
                        Time = new TimeOnly(12, 0),
                    },
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }
    }

    public DateTime? LastSentAtUtc { get; set; }

    public int ChatMessagesSinceLastSent { get; set; }
}

public enum CustomAnnouncementScheduleKind
{
    Interval,
    IntervalAfterChat,
    Weekly,
}

public interface ICustomAnnouncementScheduleEditor
{
    CustomAnnouncementScheduleKind Kind { get; }
}

public sealed class IntervalCustomAnnouncementScheduleEditor : ICustomAnnouncementScheduleEditor
{
    public CustomAnnouncementScheduleKind Kind => CustomAnnouncementScheduleKind.Interval;

    public int IntervalMinutes { get; set; } = 30;
}

public sealed class IntervalAfterChatCustomAnnouncementScheduleEditor
    : ICustomAnnouncementScheduleEditor
{
    public CustomAnnouncementScheduleKind Kind =>
        CustomAnnouncementScheduleKind.IntervalAfterChat;

    public int IntervalMinutes { get; set; } = 30;

    public int RequiredChatMessages { get; set; } = 1;
}

public sealed class WeeklyCustomAnnouncementScheduleEditor : ICustomAnnouncementScheduleEditor
{
    public CustomAnnouncementScheduleKind Kind => CustomAnnouncementScheduleKind.Weekly;

    public DayOfWeek Day { get; set; }

    public TimeOnly Time { get; set; } = new(12, 0);
}

public sealed class CustomCommandAlertSummary
{
    public int ActiveCount { get; set; }

    public List<CustomCommandAlertEditor> ActiveAlerts { get; set; } = [];
}

public sealed class CustomCommandAlertEditor
{
    public DurableAlertSeverity Severity { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? LinkPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
