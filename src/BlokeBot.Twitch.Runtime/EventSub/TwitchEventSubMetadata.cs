using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchEventSubMetadata
{
    [JsonPropertyName("message_type")]
    public string MessageType { get; init; } = string.Empty;
}
