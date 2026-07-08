using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubPayload
{
    [JsonPropertyName("event")]
    public TwitchEventSubChatMessageEvent? Event { get; init; }

    [JsonPropertyName("session")]
    public TwitchEventSubSession? Session { get; init; }
}
