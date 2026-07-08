using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record TwitchEventSubSubscriptionResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<TwitchEventSubSubscription> Data { get; init; } = [];
}
