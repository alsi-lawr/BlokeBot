using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubSession
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("reconnect_url")]
    public string? ReconnectUrl { get; init; }
}
