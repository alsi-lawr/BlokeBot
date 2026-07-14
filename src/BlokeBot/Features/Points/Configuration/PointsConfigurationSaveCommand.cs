using BlokeBot.Features.Commands;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Configuration;

public sealed record PointsCommandAliases(
    string PointsAliases,
    string GivePointsAliases,
    string AddPointsAliases,
    string RemovePointsAliases,
    string GambleAliases,
    string GiveawayAliases,
    string JoinAliases,
    string EndGiveawayAliases,
    string CancelGiveawayAliases
)
{
    internal IReadOnlyList<CommandAliasDraft> ToDrafts()
    {
        return
        [
            new(AppCommandKind.Points, PointsAliases),
            new(AppCommandKind.GivePoints, GivePointsAliases),
            new(AppCommandKind.AddPoints, AddPointsAliases),
            new(AppCommandKind.RemovePoints, RemovePointsAliases),
            new(AppCommandKind.Gamble, GambleAliases),
            new(AppCommandKind.Giveaway, GiveawayAliases),
            new(AppCommandKind.Join, JoinAliases),
            new(AppCommandKind.EndGiveaway, EndGiveawayAliases),
            new(AppCommandKind.CancelGiveaway, CancelGiveawayAliases),
        ];
    }
}

public sealed record PointsReplySettings(
    string BalanceReply,
    string OtherBalanceReply,
    string TransferReply,
    string AddReply,
    string RemoveReply,
    string InvalidAmountReply,
    string InsufficientBalanceReply,
    string ModeratorOnlyReply,
    string GamblingWinReply,
    string GamblingLoseReply,
    string GiveawayStartedReply,
    string GiveawayUpdateReply,
    string GiveawayJoinedReply,
    string GiveawayAlreadyJoinedReply,
    string GiveawayEndedReply,
    string GiveawayNoEntrantsReply,
    string GiveawayCancelledReply,
    string GiveawayAlreadyActiveReply,
    string GiveawayNotActiveReply,
    string GiveawayCooldownReply,
    string StreamOfflineReply,
    string NotEligibleReply,
    string FollowerEligibilityUnavailableReply
);

public sealed record PointsConfigurationSaveCommand
{
    internal PointsConfigurationSaveCommand(
        string pointLabel,
        PointsCommandAliases aliases,
        PointsReplySettings replies,
        ReplyDeliveryMap replyDelivery,
        bool whisperResponsesEnabled,
        int gamblingWinRatePercent,
        int gamblingCooldownSeconds,
        int giveawayDurationSeconds,
        PointAmount giveawayMinimumPayout,
        PointAmount giveawayMaximumPayout,
        int giveawayWinnerCount,
        PointsEligibilityMode giveawayEligibility,
        int giveawayCooldownSeconds
    )
    {
        PointLabel = pointLabel;
        Aliases = aliases;
        Replies = replies;
        ReplyDelivery = replyDelivery;
        WhisperResponsesEnabled = whisperResponsesEnabled;
        GamblingWinRatePercent = gamblingWinRatePercent;
        GamblingCooldownSeconds = gamblingCooldownSeconds;
        GiveawayDurationSeconds = giveawayDurationSeconds;
        GiveawayMinimumPayout = giveawayMinimumPayout;
        GiveawayMaximumPayout = giveawayMaximumPayout;
        GiveawayWinnerCount = giveawayWinnerCount;
        GiveawayEligibility = giveawayEligibility;
        GiveawayCooldownSeconds = giveawayCooldownSeconds;
    }

    public string PointLabel { get; }

    public PointsCommandAliases Aliases { get; }

    public PointsReplySettings Replies { get; }

    public ReplyDeliveryMap ReplyDelivery { get; }

    public bool WhisperResponsesEnabled { get; }

    public int GamblingWinRatePercent { get; }

    public int GamblingCooldownSeconds { get; }

    public int GiveawayDurationSeconds { get; }

    public PointAmount GiveawayMinimumPayout { get; }

    public PointAmount GiveawayMaximumPayout { get; }

    public int GiveawayWinnerCount { get; }

    public PointsEligibilityMode GiveawayEligibility { get; }

    public int GiveawayCooldownSeconds { get; }
}

public readonly record struct PointsConfigurationSaved;

public abstract record PointsConfigurationSaveFailure
{
    private PointsConfigurationSaveFailure() { }

    public abstract string Message { get; }

    public sealed record AliasAlreadyUsed(string Alias) : PointsConfigurationSaveFailure
    {
        public override string Message => $"!{Alias} is already used by another bot command.";
    }
}
