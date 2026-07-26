namespace BlokeBot.Persistence.Models;

public sealed class TwitchRewardRedemption
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string ProviderRedemptionId { get; set; } = string.Empty;
    public string ProviderRewardId { get; set; } = string.Empty;
    public string RewardTitle { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserLogin { get; set; } = string.Empty;
    public string UserInput { get; set; } = string.Empty;
    public TwitchRewardRedemptionStatus Status { get; set; }
    public DateTime RedeemedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
