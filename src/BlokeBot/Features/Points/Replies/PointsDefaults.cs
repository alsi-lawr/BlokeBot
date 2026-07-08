using BlokeBot.Features.Points.Configuration;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Points.Replies;

public static class PointsDefaults
{
    public static PointsSettings Settings() => new();

    public static PointsReplySettingsEditor Replies(PointsSettings settings) =>
        new()
        {
            BalanceReply = settings.BalanceReply,
            OtherBalanceReply = settings.OtherBalanceReply,
            TransferReply = settings.TransferReply,
            AddReply = settings.AddReply,
            RemoveReply = settings.RemoveReply,
            InvalidAmountReply = settings.InvalidAmountReply,
            InsufficientBalanceReply = settings.InsufficientBalanceReply,
            ModeratorOnlyReply = settings.ModeratorOnlyReply,
            GamblingWinReply = settings.GamblingWinReply,
            GamblingLoseReply = settings.GamblingLoseReply,
            GiveawayStartedReply = settings.GiveawayStartedReply,
            GiveawayUpdateReply = settings.GiveawayUpdateReply,
            GiveawayJoinedReply = settings.GiveawayJoinedReply,
            GiveawayAlreadyJoinedReply = settings.GiveawayAlreadyJoinedReply,
            GiveawayEndedReply = settings.GiveawayEndedReply,
            GiveawayNoEntrantsReply = settings.GiveawayNoEntrantsReply,
            GiveawayCancelledReply = settings.GiveawayCancelledReply,
            GiveawayAlreadyActiveReply = settings.GiveawayAlreadyActiveReply,
            GiveawayNotActiveReply = settings.GiveawayNotActiveReply,
            GiveawayCooldownReply = settings.GiveawayCooldownReply,
            StreamOfflineReply = settings.StreamOfflineReply,
            NotEligibleReply = settings.NotEligibleReply,
            FollowerEligibilityUnavailableReply = settings.FollowerEligibilityUnavailableReply,
        };
}
