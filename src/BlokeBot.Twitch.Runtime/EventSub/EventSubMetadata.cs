using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubMetadata
{
    [JsonPropertyName("message_type")]
    public string MessageType { get; init; } = string.Empty;
}
