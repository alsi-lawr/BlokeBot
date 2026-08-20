using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementsSectionV1(
    IReadOnlyList<MessageEntryV1> Replies,
    IReadOnlyList<AnnouncementV1> Items
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementV1(
    string Id,
    string Name,
    bool Enabled,
    string MessageReplyId,
    CustomAnnouncementDeliveryType DeliveryType,
    BlokeBot.Persistence.Models.TwitchAnnouncementColor AnnouncementColor,
    int RetryDelaySeconds,
    int OccurrenceLifetimeSeconds,
    AnnouncementScheduleV1 Schedule
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementScheduleV1(
    AnnouncementScheduleTypeV1 Type,
    int? IntervalMinutes = null,
    int? RequiredChatMessages = null,
    DayOfWeek? Day = null,
    TimeOnly? Time = null
);

public enum AnnouncementScheduleTypeV1
{
    Interval,
    IntervalAfterChat,
    Weekly,
}
