using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubIncomingRaidWireEvent
{
    [JsonPropertyName("from_broadcaster_user_id")]
    public string FromBroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("from_broadcaster_user_login")]
    public string FromBroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("from_broadcaster_user_name")]
    public string FromBroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("to_broadcaster_user_id")]
    public string ToBroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("to_broadcaster_user_login")]
    public string ToBroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("to_broadcaster_user_name")]
    public string ToBroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("viewers")]
    public int? ViewerCount { get; init; }

    internal EventSubIncomingRaidEvent? ToDomain(EventSubMetadata metadata)
    {
        if (
            string.IsNullOrWhiteSpace(metadata.MessageId)
            || metadata.MessageTimestamp is not { } messageTimestamp
            || messageTimestamp == default
            || string.IsNullOrWhiteSpace(FromBroadcasterUserId)
            || string.IsNullOrWhiteSpace(FromBroadcasterUserLogin)
            || string.IsNullOrWhiteSpace(FromBroadcasterUserName)
            || string.IsNullOrWhiteSpace(ToBroadcasterUserId)
            || string.IsNullOrWhiteSpace(ToBroadcasterUserLogin)
            || string.IsNullOrWhiteSpace(ToBroadcasterUserName)
            || ViewerCount is not { } viewerCount
            || viewerCount < 0
        )
        {
            return null;
        }

        return new(
            metadata.MessageId,
            messageTimestamp,
            FromBroadcasterUserId,
            FromBroadcasterUserLogin,
            FromBroadcasterUserName,
            ToBroadcasterUserId,
            ToBroadcasterUserLogin,
            ToBroadcasterUserName,
            viewerCount
        );
    }
}
