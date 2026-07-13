using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubChatMessageEvent
{
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
