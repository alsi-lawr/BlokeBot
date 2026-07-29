using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubEnvelope
{
    [JsonPropertyName("metadata")]
    public EventSubMetadata Metadata { get; init; } = new();

    [JsonPropertyName("payload")]
    public EventSubPayload Payload { get; init; } = new();
}
