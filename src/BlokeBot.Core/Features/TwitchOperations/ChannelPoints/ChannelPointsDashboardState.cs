namespace BlokeBot.Core.Features.TwitchOperations.ChannelPoints;

public sealed record ChannelPointsDashboardState(
    ChannelPointsAuthorizationReadiness Authorization,
    IReadOnlyList<ChannelPointsRewardView> Rewards,
    IReadOnlyList<ChannelPointsRedemptionView> ActiveRedemptions,
    IReadOnlyList<ChannelPointsRedemptionView> History
);

public sealed record ChannelPointsRewardView(
    string ProviderRewardId,
    string Title,
    string? Prompt,
    int Cost,
    bool IsManageable,
    bool IsEnabled,
    bool IsPaused,
    bool IsUserInputRequired,
    bool IsMaxPerStreamEnabled,
    int? MaxPerStream,
    bool IsMaxPerUserPerStreamEnabled,
    int? MaxPerUserPerStream,
    bool IsGlobalCooldownEnabled,
    int? GlobalCooldownSeconds,
    bool ShouldRedemptionsSkipRequestQueue,
    string? BackgroundColor
);

public sealed record ChannelPointsRedemptionView(
    string ProviderRedemptionId,
    string ProviderRewardId,
    string RewardTitle,
    string UserLogin,
    string UserInput,
    string Status,
    DateTime RedeemedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsManageable
);

public abstract record ChannelPointsAuthorizationReadiness
{
    private ChannelPointsAuthorizationReadiness() { }

    public sealed record Ready : ChannelPointsAuthorizationReadiness;

    public sealed record NeedsBroadcasterAuthorization(string Message)
        : ChannelPointsAuthorizationReadiness
    {
        public string ReauthorizationUrl => "/oauth/broadcaster/start";
    }
}
