using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchEventSubChatMessageEvent
{
    [JsonPropertyName("badges")]
    public IReadOnlyList<TwitchEventSubBadge> Badges { get; init; } = [];

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("chatter_user_login")]
    public string ChatterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public TwitchEventSubChatMessage? Message { get; init; }

    [JsonPropertyName("message_id")]
    public string MessageId { get; init; } = string.Empty;
}
