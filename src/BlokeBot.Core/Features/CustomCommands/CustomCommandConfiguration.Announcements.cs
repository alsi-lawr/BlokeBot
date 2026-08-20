using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomAnnouncementEditor
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int MessageLibraryEntryId { get; set; }

    public CustomAnnouncementDeliveryType DeliveryType { get; set; } =
        CustomAnnouncementDeliveryType.ChatMessage;

    public BlokeBot.Persistence.Models.TwitchAnnouncementColor AnnouncementColor { get; set; } =
        BlokeBot.Persistence.Models.TwitchAnnouncementColor.Primary;

    public CustomAnnouncementLatestDeliveryResult LatestDeliveryResult { get; set; } =
        CustomAnnouncementLatestDeliveryResult.None;

    public int RetryDelaySeconds { get; set; } = 2;

    public int OccurrenceLifetimeSeconds { get; set; } = 30;

    public ICustomAnnouncementScheduleEditor Schedule
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new IntervalCustomAnnouncementScheduleEditor();

    public CustomAnnouncementScheduleKind ScheduleKind
    {
        get => Schedule.Kind;
        set
        {
            if (value == Schedule.Kind)
            {
                return;
            }

            var intervalMinutes = Schedule switch
            {
                IntervalCustomAnnouncementScheduleEditor interval => interval.IntervalMinutes,
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
                CustomAnnouncementScheduleKind.Weekly => new WeeklyCustomAnnouncementScheduleEditor
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

public enum TwitchAnnouncementAvailability
{
    Available,
    ReconnectRequired,
    AuthorityRequired,
    Unavailable,
}

public sealed record TwitchAnnouncementReadiness(
    TwitchAnnouncementAvailability Availability,
    string BotLogin
);

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
    public CustomAnnouncementScheduleKind Kind => CustomAnnouncementScheduleKind.IntervalAfterChat;

    public int IntervalMinutes { get; set; } = 30;

    public int RequiredChatMessages { get; set; } = 1;
}

public sealed class WeeklyCustomAnnouncementScheduleEditor : ICustomAnnouncementScheduleEditor
{
    public CustomAnnouncementScheduleKind Kind => CustomAnnouncementScheduleKind.Weekly;

    public DayOfWeek Day { get; set; }

    public TimeOnly Time { get; set; } = new(12, 0);

    internal DayOfWeek ProjectedDay { get; set; }

    internal TimeOnly ProjectedTime { get; set; } = new(12, 0);

    internal DayOfWeek UtcDay { get; set; }

    internal TimeOnly UtcTime { get; set; } = new(12, 0);

    internal bool HasUtcSchedule { get; set; }
}
