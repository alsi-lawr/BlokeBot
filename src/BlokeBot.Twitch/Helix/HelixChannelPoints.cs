using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record HelixCustomReward(
    string Id,
    string Title,
    string? Prompt,
    int Cost,
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

public sealed record HelixCustomRewardDraft(
    string Title,
    string? Prompt,
    int Cost,
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

public sealed record HelixRewardRedemption(
    string Id,
    string RewardId,
    string RewardTitle,
    string UserId,
    string UserLogin,
    string UserInput,
    HelixRewardRedemptionStatus Status,
    DateTimeOffset RedeemedAt
);

public enum HelixRewardRedemptionStatus
{
    Unfulfilled,
    Fulfilled,
    Canceled,
    Unknown,
}

public abstract record HelixChannelPointsOutcome
{
    private HelixChannelPointsOutcome() { }

    public sealed record Success : HelixChannelPointsOutcome;

    public sealed record Unauthorized : HelixChannelPointsOutcome;

    public sealed record Ineligible : HelixChannelPointsOutcome;

    public sealed record ExternalReward : HelixChannelPointsOutcome;

    public sealed record Unavailable : HelixChannelPointsOutcome;
}

public abstract record HelixCustomRewardsLookupOutcome
{
    private HelixCustomRewardsLookupOutcome() { }

    public sealed record Found(IReadOnlyList<HelixCustomReward> Rewards)
        : HelixCustomRewardsLookupOutcome;

    public sealed record Unavailable : HelixCustomRewardsLookupOutcome;

    public sealed record Ineligible : HelixCustomRewardsLookupOutcome;

    public sealed record Unauthorized : HelixCustomRewardsLookupOutcome;
}

public abstract record HelixRewardRedemptionsLookupOutcome
{
    private HelixRewardRedemptionsLookupOutcome() { }

    public sealed record Found(HelixRewardRedemptionsPage Page)
        : HelixRewardRedemptionsLookupOutcome;

    public sealed record Unavailable : HelixRewardRedemptionsLookupOutcome;

    public sealed record Ineligible : HelixRewardRedemptionsLookupOutcome;

    public sealed record Unauthorized : HelixRewardRedemptionsLookupOutcome;
}

public sealed record HelixRewardRedemptionsPage(
    IReadOnlyList<HelixRewardRedemption> Redemptions,
    string? Cursor
);

internal sealed record HelixCustomRewardsResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixCustomRewardWire> Data { get; init; } = [];
}

internal sealed record HelixCustomRewardWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("cost")]
    public int Cost { get; init; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; }

    [JsonPropertyName("is_paused")]
    public bool IsPaused { get; init; }

    [JsonPropertyName("is_user_input_required")]
    public bool IsUserInputRequired { get; init; }

    [JsonPropertyName("max_per_stream_setting")]
    public HelixRewardLimitWire MaxPerStream { get; init; } = new();

    [JsonPropertyName("max_per_user_per_stream_setting")]
    public HelixRewardLimitWire MaxPerUserPerStream { get; init; } = new();

    [JsonPropertyName("global_cooldown_setting")]
    public HelixRewardCooldownWire GlobalCooldown { get; init; } = new();

    [JsonPropertyName("should_redemptions_skip_request_queue")]
    public bool ShouldRedemptionsSkipRequestQueue { get; init; }

    [JsonPropertyName("background_color")]
    public string? BackgroundColor { get; init; }

    public HelixCustomReward ToDomain()
    {
        return new(
            Id,
            Title,
            Prompt,
            Cost,
            IsEnabled,
            IsPaused,
            IsUserInputRequired,
            MaxPerStream.IsEnabled,
            MaxPerStream.MaxPerStream,
            MaxPerUserPerStream.IsEnabled,
            MaxPerUserPerStream.MaxPerUserPerStream,
            GlobalCooldown.IsEnabled,
            GlobalCooldown.GlobalCooldownSeconds,
            ShouldRedemptionsSkipRequestQueue,
            BackgroundColor
        );
    }
}

internal sealed record HelixRewardLimitWire
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; }

    [JsonPropertyName("max_per_stream")]
    public int? MaxPerStream { get; init; }

    [JsonPropertyName("max_per_user_per_stream")]
    public int? MaxPerUserPerStream { get; init; }
}

internal sealed record HelixRewardCooldownWire
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; }

    [JsonPropertyName("global_cooldown_seconds")]
    public int? GlobalCooldownSeconds { get; init; }
}

internal sealed record HelixRewardRedemptionsResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixRewardRedemptionWire> Data { get; init; } = [];

    [JsonPropertyName("pagination")]
    public HelixRewardRedemptionsPaginationWire Pagination { get; init; } = new();
}

internal sealed record HelixRewardRedemptionsPaginationWire
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

internal sealed record HelixRewardRedemptionWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("reward")]
    public HelixRedemptionRewardWire Reward { get; init; } = new();

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("user_login")]
    public string UserLogin { get; init; } = string.Empty;

    [JsonPropertyName("user_input")]
    public string UserInput { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("redeemed_at")]
    public DateTimeOffset RedeemedAt { get; init; }

    public HelixRewardRedemption ToDomain()
    {
        return new(
            Id,
            Reward.Id,
            Reward.Title,
            UserId,
            UserLogin,
            UserInput,
            Status switch
            {
                "UNFULFILLED" => HelixRewardRedemptionStatus.Unfulfilled,
                "FULFILLED" => HelixRewardRedemptionStatus.Fulfilled,
                "CANCELED" => HelixRewardRedemptionStatus.Canceled,
                _ => HelixRewardRedemptionStatus.Unknown,
            },
            RedeemedAt
        );
    }
}

internal sealed record HelixRedemptionRewardWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
}
