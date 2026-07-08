using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchEventSubSubscriptionResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<TwitchEventSubSubscription> Data { get; init; } = [];
}
