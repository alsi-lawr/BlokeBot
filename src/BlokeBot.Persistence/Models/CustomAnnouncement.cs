namespace BlokeBot.Persistence.Models;

public sealed class CustomAnnouncement
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int MessageLibraryEntryId { get; set; }

    public CustomAnnouncementScheduleType ScheduleType { get; set; } =
        CustomAnnouncementScheduleType.Interval;

    public int IntervalMinutes { get; set; } = 30;

    public int RequiredChatMessages { get; set; }

    public DayOfWeek? WeeklyDay { get; set; }

    public TimeOnly? WeeklyTime { get; set; }

    public DateTime? LastSentAtUtc { get; set; }

    public int ChatMessagesSinceLastSent { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public CustomMessageLibraryEntry? MessageLibraryEntry { get; set; }
}
