using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record ChatMessageSendResult
{
    [JsonPropertyName("drop_reason")]
    public ChatMessageDropReason? DropReason { get; init; }

    [JsonPropertyName("is_sent")]
    public required bool IsSent { get; init; }

    [JsonPropertyName("message_id")]
    public required string MessageId { get; init; }

    public override string ToString() =>
        $"{nameof(ChatMessageSendResult)} {{ IsSent = {IsSent}, DropReason = {DropReason} }}";
}
