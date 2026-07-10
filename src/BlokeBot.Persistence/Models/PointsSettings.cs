namespace BlokeBot.Persistence.Models;

public sealed class PointsSettings
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string PointLabel { get; set; } = "points";

    public int GamblingWinRatePercent { get; set; } = 50;

    public int GamblingCooldownSeconds { get; set; }

    public int GiveawayDurationSeconds { get; set; } = 300;

    public string GiveawayMinimumPayout { get; set; } = "10";

    public string GiveawayMaximumPayout { get; set; } = "100";

    public int GiveawayWinnerCount { get; set; } = 1;

    public PointsEligibilityMode GiveawayEligibility { get; set; } = PointsEligibilityMode.Everyone;

    public int GiveawayCooldownSeconds { get; set; } = 300;

    public string BalanceReply { get; set; } = "@{user}, you have {balance} {label}.";

    public string OtherBalanceReply { get; set; } = "{user} has {balance} {label}.";

    public string TransferReply { get; set; } = "{from} sent {amount} {label} to {to}.";

    public string AddReply { get; set; } = "Added {amount} {label} to {user}.";

    public string RemoveReply { get; set; } = "Removed {amount} {label} from {user}.";

    public string InvalidAmountReply { get; set; } = "That point amount is not valid.";

    public string InsufficientBalanceReply { get; set; } = "You do not have enough {label}.";

    public string ModeratorOnlyReply { get; set; } = "Only moderators can use that command.";

    public string GamblingWinReply { get; set; } =
        "{user} gambled {amount} {label} and won. Balance: {balance}.";

    public string GamblingLoseReply { get; set; } =
        "{user} gambled {amount} {label} and lost. Balance: {balance}.";

    public string GiveawayStartedReply { get; set; } = "Giveaway started. Type !join to enter.";

    public string GiveawayUpdateReply { get; set; } =
        "Giveaway closes in {time_left}. Type !join to enter.";

    public string GiveawayJoinedReply { get; set; } = "{user} entered the giveaway.";

    public string GiveawayAlreadyJoinedReply { get; set; } = "{user}, you are already entered.";

    public string GiveawayEndedReply { get; set; } = "Giveaway winners: {winners}.";

    public string GiveawayNoEntrantsReply { get; set; } =
        "Giveaway ended with no eligible entrants.";

    public string GiveawayCancelledReply { get; set; } = "Giveaway cancelled.";

    public string GiveawayAlreadyActiveReply { get; set; } = "A giveaway is already active.";

    public string GiveawayNotActiveReply { get; set; } = "No giveaway is active.";

    public string GiveawayCooldownReply { get; set; } =
        "Giveaways are on cooldown. Try again in {time_left}.";

    public string StreamOfflineReply { get; set; } =
        "Giveaways can only start while the stream is live.";

    public string NotEligibleReply { get; set; } =
        "{user}, you are not eligible for this giveaway.";

    public string FollowerEligibilityUnavailableReply { get; set; } =
        "Follower eligibility is not available for this channel.";
}
