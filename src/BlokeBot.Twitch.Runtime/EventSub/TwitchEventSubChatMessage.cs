using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchEventSubChatMessage
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
