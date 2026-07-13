using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubBadge
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("set_id")]
    public string SetId { get; init; } = string.Empty;
}
