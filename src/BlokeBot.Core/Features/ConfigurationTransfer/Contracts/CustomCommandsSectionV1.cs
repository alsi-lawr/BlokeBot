using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomCommandsSectionV1(
    string TimeZoneId,
    IReadOnlyList<MessageEntryV1> Replies,
    IReadOnlyList<CounterV1> Counters,
    IReadOnlyList<CustomCommandV1> Commands
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MessageEntryV1(
    string Id,
    string Name,
    CustomMessageSelectionMode SelectionMode,
    IReadOnlyList<string> Variants
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CounterV1(string Id, string Name, long Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomCommandV1(
    string Id,
    string Name,
    bool Enabled,
    IReadOnlyList<string> Aliases,
    bool AllowEveryone,
    bool AllowModerators,
    IReadOnlyList<AllowedUserV1> AllowedUsers,
    int CooldownSeconds,
    CustomCommandCooldownScope CooldownScope,
    CustomCommandInvocationLimit InvocationLimit,
    CustomCommandActionV1 Action
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllowedUserV1(string TwitchUserId, string Login, string DisplayName);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomCommandActionV1(
    CustomCommandActionTypeV1 Type,
    string? ZeroArgumentReplyId = null,
    string? OneArgumentReplyId = null,
    string? TwoArgumentReplyId = null,
    string? CounterId = null
);

public enum CustomCommandActionTypeV1
{
    Message,
    Counter,
    Automation,
    OverlayCue,
}
