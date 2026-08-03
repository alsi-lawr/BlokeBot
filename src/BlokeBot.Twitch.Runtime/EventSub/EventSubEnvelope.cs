using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubEnvelope
{
    [JsonPropertyName("subscription")]
    public JsonElement? Subscription { get; init; }

    [JsonPropertyName("event")]
    public JsonElement? Event { get; init; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; init; }

    [JsonIgnore]
    public EventSubMetadata Metadata { get; set; } = new();

    [JsonIgnore]
    public EventSubPayload Payload => new() { Event = Event, Challenge = Challenge };
}
