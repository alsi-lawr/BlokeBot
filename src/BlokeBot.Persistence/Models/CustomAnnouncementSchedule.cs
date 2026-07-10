namespace BlokeBot.Persistence.Models;

public abstract class CustomAnnouncementSchedule
{
    public int CustomAnnouncementId { get; set; }

    public int HostId { get; set; }

    public CustomAnnouncement? Announcement { get; set; }
}

public sealed class IntervalCustomAnnouncementSchedule : CustomAnnouncementSchedule
{
    public const string Discriminator = "Interval";

    public int IntervalMinutes { get; set; } = 30;
}

public sealed class IntervalAfterChatCustomAnnouncementSchedule : CustomAnnouncementSchedule
{
    public const string Discriminator = "IntervalAfterChat";

    public int IntervalMinutes { get; set; } = 30;

    public int RequiredChatMessages { get; set; } = 1;
}

public sealed class WeeklyCustomAnnouncementSchedule : CustomAnnouncementSchedule
{
    public const string Discriminator = "Weekly";

    public DayOfWeek Day { get; set; }

    public TimeOnly Time { get; set; }
}
