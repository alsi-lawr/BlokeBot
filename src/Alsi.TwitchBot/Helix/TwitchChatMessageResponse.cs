using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchChatMessageResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<TwitchChatMessageSendResult> Data { get; init; } = [];
}
