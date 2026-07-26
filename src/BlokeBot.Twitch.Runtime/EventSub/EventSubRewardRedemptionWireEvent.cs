using System.Text.Json.Serialization;
using BlokeBot.Twitch;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubRewardRedemptionWireEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("reward")]
    public RedemptionRewardWire Reward { get; init; } = new();

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

    public EventSubRewardRedemptionEvent ToDomain(string messageId)
    {
        return new(
            BroadcasterUserId,
            BroadcasterUserLogin,
            Id,
            Reward.Id,
            Reward.Title,
            UserId,
            UserLogin,
            UserInput,
            Status switch
            {
                "unfulfilled" => HelixRewardRedemptionStatus.Unfulfilled,
                "fulfilled" => HelixRewardRedemptionStatus.Fulfilled,
                "canceled" => HelixRewardRedemptionStatus.Canceled,
                _ => HelixRewardRedemptionStatus.Unknown,
            },
            RedeemedAt,
            messageId
        );
    }

    internal sealed record RedemptionRewardWire
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;
    }
}
