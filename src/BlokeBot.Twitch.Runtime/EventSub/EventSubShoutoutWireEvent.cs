using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubShoutoutWireEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("from_broadcaster_user_id")]
    public string FromBroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("from_broadcaster_user_login")]
    public string FromBroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("to_broadcaster_user_id")]
    public string ToBroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("to_broadcaster_user_login")]
    public string ToBroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("viewer_count")]
    public int ViewerCount { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("cooldown_ends_at")]
    public DateTimeOffset? CooldownEndsAt { get; init; }

    [JsonPropertyName("target_cooldown_ends_at")]
    public DateTimeOffset? TargetCooldownEndsAt { get; init; }

    internal EventSubShoutoutEvent ToDomain(
        EventSubShoutoutDirection direction,
        string messageId
    ) =>
        new(
            BroadcasterUserId,
            BroadcasterUserLogin,
            FromBroadcasterUserId,
            FromBroadcasterUserLogin,
            ToBroadcasterUserId,
            ToBroadcasterUserLogin,
            ViewerCount,
            StartedAt,
            CooldownEndsAt,
            TargetCooldownEndsAt,
            direction,
            messageId
        );
}
