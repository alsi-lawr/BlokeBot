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
    int RewardCost,
    string UserId,
    string UserLogin,
    string UserName,
    string UserInput,
    HelixRewardRedemptionStatus Status,
    DateTimeOffset RedeemedAt,
    string MessageId,
    bool IsNewRedemption
)
{
    public HelixRewardRedemption ToHelix() =>
        new(RedemptionId, RewardId, RewardTitle, UserId, UserLogin, UserInput, Status, RedeemedAt);
}
