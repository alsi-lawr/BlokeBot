using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchChatMessageSendResult
{
    [JsonPropertyName("drop_reason")]
    public TwitchChatMessageDropReason? DropReason { get; init; }

    [JsonPropertyName("is_sent")]
    public bool IsSent { get; init; }

    [JsonPropertyName("message_id")]
    public string MessageId { get; init; } = string.Empty;
}
