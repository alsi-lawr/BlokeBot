using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubSubscription
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}
