using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubChatMessageEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("source_broadcaster_user_id")]
    public string? SourceBroadcasterUserId { get; init; }

    [JsonPropertyName("chatter_user_name")]
    public string ChatterUserName { get; init; } = string.Empty;

    [JsonPropertyName("badges")]
    public IReadOnlyList<EventSubBadge> Badges { get; init; } = [];

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("chatter_user_login")]
    public string ChatterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("chatter_user_id")]
    public string ChatterUserId { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public EventSubChatMessage? Message { get; init; }

    [JsonPropertyName("message_id")]
    public string MessageId { get; init; } = string.Empty;
}
