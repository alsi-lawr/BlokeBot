using BlokeBot.Twitch;

namespace BlokeBot.Twitch.Runtime;

public interface IChannelPointsEventObserver
{
    Task RedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellationToken
    );
}

public sealed record EventSubRewardRedemptionEvent(
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string RedemptionId,
    string RewardId,
    string RewardTitle,
    string UserId,
    string UserLogin,
    string UserInput,
    HelixRewardRedemptionStatus Status,
    DateTimeOffset RedeemedAt,
    string MessageId
)
{
    public HelixRewardRedemption ToHelix()
    {
        return new(
            RedemptionId,
            RewardId,
            RewardTitle,
            UserId,
            UserLogin,
            UserInput,
            Status,
            RedeemedAt
        );
    }
}
