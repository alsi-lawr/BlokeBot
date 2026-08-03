using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubPayload
{
    [JsonPropertyName("event")]
    public JsonElement? Event { get; init; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; init; }
}
