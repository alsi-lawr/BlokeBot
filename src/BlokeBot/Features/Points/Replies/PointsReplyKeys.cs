namespace BlokeBot.Features.Points.Replies;

public static class PointsReplyKeys
{
    public const string Balance = "balance";
    public const string OtherBalance = "other_balance";
    public const string Transfer = "transfer";
    public const string Add = "add";
    public const string Remove = "remove";
    public const string InvalidAmount = "invalid_amount";
    public const string InsufficientBalance = "insufficient_balance";
    public const string ModeratorOnly = "moderator_only";
    public const string GiveawayJoined = "giveaway_joined";
    public const string GiveawayAlreadyJoined = "giveaway_already_joined";
    public const string GiveawayAlreadyActive = "giveaway_already_active";
    public const string GiveawayNotActive = "giveaway_not_active";
    public const string GiveawayCooldown = "giveaway_cooldown";
    public const string StreamOffline = "stream_offline";
    public const string NotEligible = "not_eligible";
    public const string FollowerEligibilityUnavailable = "follower_eligibility_unavailable";

    public static readonly IReadOnlyList<string> WhisperableKeys =
    [
        Balance,
        OtherBalance,
        Transfer,
        Add,
        Remove,
        InvalidAmount,
        InsufficientBalance,
        ModeratorOnly,
        GiveawayJoined,
        GiveawayAlreadyJoined,
        GiveawayAlreadyActive,
        GiveawayNotActive,
        GiveawayCooldown,
        StreamOffline,
        NotEligible,
        FollowerEligibilityUnavailable,
    ];
}
