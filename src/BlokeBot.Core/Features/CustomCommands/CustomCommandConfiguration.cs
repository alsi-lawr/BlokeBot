using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandConfiguration
{
    public string TimeZoneId { get; set; } = "UTC";

    public List<CustomMessageLibraryEntryEditor> MessageEntries { get; set; } = [];

    public List<CustomCommandEditor> Commands { get; set; } = [];

    public List<CustomCounterEditor> Counters { get; set; } = [];

    public List<CustomAnnouncementEditor> Announcements { get; set; } = [];

    public TwitchAnnouncementReadiness TwitchAnnouncementReadiness { get; set; } =
        new(TwitchAnnouncementAvailability.Unavailable, string.Empty);

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

    public CustomCommandInvocationLimit InvocationLimit { get; set; } =
        CustomCommandInvocationLimit.Unlimited;

    public string ResetViewerLogin { get; set; } = string.Empty;

    public ICustomCommandActionEditor Action
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new MessageCustomCommandActionEditor();

    public CustomCommandActionKind ActionKind
    {
        get => Action.Kind;
        set
        {
            if (value == Action.Kind)
            {
                return;
            }

            Action = value switch
            {
                CustomCommandActionKind.Message => new MessageCustomCommandActionEditor
                {
                    ReplyRoutes = Action.ReplyRoutes,
                },
                CustomCommandActionKind.Counter => new CounterCustomCommandActionEditor
                {
                    ReplyRoutes = Action.ReplyRoutes,
                },
                CustomCommandActionKind.OverlayCue => new OverlayCueCustomCommandActionEditor
                {
                    ReplyRoutes = Action.ReplyRoutes,
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
    OverlayCue,
}

public interface ICustomCommandActionEditor
{
    CustomCommandActionKind Kind { get; }

    CustomCommandReplyRoutesEditor ReplyRoutes { get; set; }
}

public sealed class CustomCommandReplyRoutesEditor
{
    public int? ZeroArgumentMessageLibraryEntryId { get; set; }

    public int? OneArgumentMessageLibraryEntryId { get; set; }

    public int? TwoArgumentMessageLibraryEntryId { get; set; }
}

public sealed class MessageCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Message;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();
}

public sealed class CounterCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.Counter;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    public int CounterId { get; set; }
}

public sealed class OverlayCueCustomCommandActionEditor : ICustomCommandActionEditor
{
    public CustomCommandActionKind Kind => CustomCommandActionKind.OverlayCue;

    public CustomCommandReplyRoutesEditor ReplyRoutes
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new();

    public Guid TargetOverlayPublicId { get; set; }

    public Guid CuePublicId { get; set; }

    public OverlayCueQueuePolicy QueuePolicy { get; set; } = OverlayCueQueuePolicy.Enqueue;

    public OverlayCueReplyOrder ReplyOrder { get; set; } = OverlayCueReplyOrder.After;
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
