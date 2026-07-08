using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchUserLookupResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<TwitchUserLookupUser> Data { get; init; } = [];
}
