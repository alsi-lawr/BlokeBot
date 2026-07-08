using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubChatMessage
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
