using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubPollWireEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("choices")]
    public IReadOnlyList<Choice> Choices { get; init; } = [];

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("ends_at")]
    public DateTimeOffset? EndsAt { get; init; }

    internal EventSubPollEvent ToDomain(string messageId) =>
        new(
            BroadcasterUserId,
            BroadcasterUserLogin,
            Id,
            Title,
            Choices
                .Select(static x => new EventSubPollChoice(
                    x.Id,
                    x.Title,
                    x.Votes,
                    x.ChannelPointsVotes
                ))
                .ToArray(),
            Status,
            StartedAt,
            EndsAt,
            messageId
        );

    internal sealed record Choice
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("votes")]
        public int Votes { get; init; }

        [JsonPropertyName("channel_points_votes")]
        public int ChannelPointsVotes { get; init; }
    }
}
