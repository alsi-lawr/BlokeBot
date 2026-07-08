using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchEventSubSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}
