using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubChatMessage
{
    [JsonPropertyName("fragments")]
    public IReadOnlyList<EventSubChatFragment> Fragments { get; init; } = [];

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

internal sealed record EventSubChatFragment
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("emote")]
    public EventSubChatEmote? Emote { get; init; }
}

internal sealed record EventSubChatEmote
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
}
