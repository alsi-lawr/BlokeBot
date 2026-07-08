using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubEnvelope
{
    [JsonPropertyName("metadata")]
    public TwitchEventSubMetadata Metadata { get; init; } = new();

    [JsonPropertyName("payload")]
    public TwitchEventSubPayload Payload { get; init; } = new();
}
