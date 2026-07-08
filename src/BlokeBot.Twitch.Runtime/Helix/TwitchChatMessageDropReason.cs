using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchChatMessageDropReason
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
