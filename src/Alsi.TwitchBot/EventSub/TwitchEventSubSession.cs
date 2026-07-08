using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubSession
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("reconnect_url")]
    public string? ReconnectUrl { get; init; }
}
