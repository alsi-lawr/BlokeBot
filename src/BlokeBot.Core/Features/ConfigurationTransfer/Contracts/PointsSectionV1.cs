using System.Text.Json.Serialization;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PointsSectionV1(
    string PointLabel,
    IReadOnlyList<CommandAliasesV1> CommandAliases,
    PointsRepliesV1 Replies,
    int GamblingWinRatePercent,
    int GamblingCooldownSeconds,
    int GiveawayDurationSeconds,
    string GiveawayMinimumPayout,
    string GiveawayMaximumPayout,
    int GiveawayWinnerCount,
    PointsEligibilityMode GiveawayEligibility,
    int GiveawayCooldownSeconds
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PointsRepliesV1(
    string Balance,
    string OtherBalance,
    string Transfer,
    string Add,
    string Remove,
    string InvalidAmount,
    string InsufficientBalance,
    string ModeratorOnly,
    string GamblingWin,
    string GamblingLose,
    string GiveawayStarted,
    string GiveawayUpdate,
    string GiveawayJoined,
    string GiveawayAlreadyJoined,
    string GiveawayEnded,
    string GiveawayNoEntrants,
    string GiveawayCancelled,
    string GiveawayAlreadyActive,
    string GiveawayNotActive,
    string GiveawayCooldown,
    string StreamOffline,
    string NotEligible,
    string FollowerEligibilityUnavailable
);
