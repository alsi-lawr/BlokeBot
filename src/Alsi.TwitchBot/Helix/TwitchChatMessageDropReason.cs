using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchChatMessageDropReason
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
