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
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Aliases { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool ModeratorOnly { get; set; }

    public int CooldownSeconds { get; set; }

    public CustomCommandCooldownScope CooldownScope { get; set; } =
        CustomCommandCooldownScope.Global;

    public CustomCommandActionType ActionType { get; set; } = CustomCommandActionType.Message;

    public int MessageLibraryEntryId { get; set; }

    public int? CounterId { get; set; }
}

public sealed class CustomCounterEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Value { get; set; }
}

public sealed class CustomAnnouncementEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int MessageLibraryEntryId { get; set; }

    public CustomAnnouncementScheduleType ScheduleType { get; set; } =
        CustomAnnouncementScheduleType.Interval;

    public int IntervalMinutes { get; set; } = 30;

    public int RequiredChatMessages { get; set; }

    public DayOfWeek? WeeklyDay { get; set; }

    public string WeeklyTime { get; set; } = string.Empty;

    public DateTime? LastSentAtUtc { get; set; }

    public int ChatMessagesSinceLastSent { get; set; }
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
