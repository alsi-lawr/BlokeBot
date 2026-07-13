using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubChatMessage
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
