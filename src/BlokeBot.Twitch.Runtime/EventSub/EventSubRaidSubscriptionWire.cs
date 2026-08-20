using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubRaidSubscriptionWire
{
    [JsonPropertyName("condition")]
    public EventSubRaidSubscriptionConditionWire? Condition { get; init; }
}

internal sealed record EventSubRaidSubscriptionConditionWire
{
    [JsonPropertyName("from_broadcaster_user_id")]
    public string? FromBroadcasterUserId { get; init; }

    [JsonPropertyName("to_broadcaster_user_id")]
    public string? ToBroadcasterUserId { get; init; }
}
