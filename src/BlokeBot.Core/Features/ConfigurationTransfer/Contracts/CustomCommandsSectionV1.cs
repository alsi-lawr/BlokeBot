using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomCommandsSectionV1(
    [property: JsonRequired] string TimeZoneId,
    [property: JsonRequired] IReadOnlyList<MessageEntryV1> Replies,
    [property: JsonRequired] IReadOnlyList<CounterV1> Counters,
    [property: JsonRequired] IReadOnlyList<CustomCommandV1> Commands
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MessageEntryV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] CustomMessageSelectionMode SelectionMode,
    [property: JsonRequired] IReadOnlyList<string> Variants
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] long Value
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomCommandV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] IReadOnlyList<string> Aliases,
    [property: JsonRequired] bool AllowEveryone,
    [property: JsonRequired] bool AllowModerators,
    [property: JsonRequired] int CooldownSeconds,
    [property: JsonRequired] CustomCommandCooldownScope CooldownScope,
    [property: JsonRequired] CustomCommandInvocationLimit InvocationLimit,
    [property: JsonRequired] CustomCommandActionV1 Action
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomCommandActionV1(
    [property: JsonRequired] CustomCommandActionTypeV1 Type,
    string? ZeroArgumentReplyId = null,
    string? OneArgumentReplyId = null,
    string? TwoArgumentReplyId = null,
    string? CounterId = null,
    string? OverlayTargetId = null,
    string? OverlayTargetName = null,
    string? OverlayCueId = null,
    string? OverlayCueName = null,
    OverlayCueQueuePolicy? OverlayQueuePolicy = null,
    OverlayCueReplyOrder? OverlayReplyOrder = null
);

public enum CustomCommandActionTypeV1
{
    Message,
    Counter,
    Automation,
    OverlayCue,
}
