using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PointsSectionV1(
    [property: JsonRequired] string PointLabel,
    [property: JsonRequired] IReadOnlyList<CommandAliasesV1> CommandAliases,
    [property: JsonRequired] PointsRepliesV1 Replies,
    [property: JsonRequired] int GamblingWinRatePercent,
    [property: JsonRequired] int GamblingCooldownSeconds,
    [property: JsonRequired] int GiveawayDurationSeconds,
    [property: JsonRequired] string GiveawayMinimumPayout,
    [property: JsonRequired] string GiveawayMaximumPayout,
    [property: JsonRequired] int GiveawayWinnerCount,
    [property: JsonRequired] PointsEligibilityMode GiveawayEligibility,
    [property: JsonRequired] int GiveawayCooldownSeconds
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PointsRepliesV1(
    [property: JsonRequired] string Balance,
    [property: JsonRequired] string OtherBalance,
    [property: JsonRequired] string Transfer,
    [property: JsonRequired] string Add,
    [property: JsonRequired] string Remove,
    [property: JsonRequired] string InvalidAmount,
    [property: JsonRequired] string InsufficientBalance,
    [property: JsonRequired] string ModeratorOnly,
    [property: JsonRequired] string GamblingWin,
    [property: JsonRequired] string GamblingLose,
    [property: JsonRequired] string GiveawayStarted,
    [property: JsonRequired] string GiveawayUpdate,
    [property: JsonRequired] string GiveawayJoined,
    [property: JsonRequired] string GiveawayAlreadyJoined,
    [property: JsonRequired] string GiveawayEnded,
    [property: JsonRequired] string GiveawayNoEntrants,
    [property: JsonRequired] string GiveawayCancelled,
    [property: JsonRequired] string GiveawayAlreadyActive,
    [property: JsonRequired] string GiveawayNotActive,
    [property: JsonRequired] string GiveawayCooldown,
    [property: JsonRequired] string StreamOffline,
    [property: JsonRequired] string NotEligible,
    [property: JsonRequired] string FollowerEligibilityUnavailable
);
