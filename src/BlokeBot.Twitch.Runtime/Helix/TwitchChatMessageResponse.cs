using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchChatMessageResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<TwitchChatMessageSendResult> Data { get; init; } = [];
}
