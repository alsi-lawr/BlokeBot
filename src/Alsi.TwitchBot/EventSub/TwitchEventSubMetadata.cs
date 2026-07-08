using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubMetadata
{
    [JsonPropertyName("message_type")]
    public string MessageType { get; init; } = string.Empty;
}
