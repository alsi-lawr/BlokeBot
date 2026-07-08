using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchUserLookupUser
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;
}
