using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementsSectionV1(
    [property: JsonRequired] IReadOnlyList<MessageEntryV1> Replies,
    [property: JsonRequired] IReadOnlyList<AnnouncementV1> Items
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] string MessageReplyId,
    [property: JsonRequired] CustomAnnouncementDeliveryType DeliveryType,
    [property: JsonRequired] BlokeBot.Persistence.Models.TwitchAnnouncementColor AnnouncementColor,
    [property: JsonRequired] int RetryDelaySeconds,
    [property: JsonRequired] int OccurrenceLifetimeSeconds,
    [property: JsonRequired] AnnouncementScheduleV1 Schedule
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementScheduleV1(
    [property: JsonRequired] AnnouncementScheduleTypeV1 Type,
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
