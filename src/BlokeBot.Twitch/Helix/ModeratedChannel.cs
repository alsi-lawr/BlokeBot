using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record ModeratedChannel
{
    [JsonPropertyName("broadcaster_id")]
    public string BroadcasterId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_login")]
    public string BroadcasterLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_name")]
    public string BroadcasterName { get; init; } = string.Empty;
}
