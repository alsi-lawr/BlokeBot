using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

public sealed record TwitchModeratedChannel
{
    [JsonPropertyName("broadcaster_id")]
    public string BroadcasterId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_login")]
    public string BroadcasterLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_name")]
    public string BroadcasterName { get; init; } = string.Empty;
}
