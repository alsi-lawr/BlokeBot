using System.Text.Json.Serialization;

namespace BlokeBot.Auth.Moderation;

internal sealed class ModeratedChannelData
{
    [JsonPropertyName("broadcaster_id")]
    public string? BroadcasterId { get; init; }

    [JsonPropertyName("broadcaster_login")]
    public string? BroadcasterLogin { get; init; }

    [JsonPropertyName("broadcaster_name")]
    public string? BroadcasterName { get; init; }
}
