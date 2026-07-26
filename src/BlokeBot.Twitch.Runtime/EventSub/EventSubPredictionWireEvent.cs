using System.Text.Json.Serialization;
using BlokeBot.Twitch;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubPredictionWireEvent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("outcomes")]
    public IReadOnlyList<Outcome> Outcomes { get; init; } = [];

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("locks_at")]
    public DateTimeOffset? LocksAt { get; init; }

    [JsonPropertyName("locked_at")]
    public DateTimeOffset? LockedAt { get; init; }

    [JsonPropertyName("ended_at")]
    public DateTimeOffset? EndedAt { get; init; }

    [JsonPropertyName("winning_outcome_id")]
    public string? WinningOutcomeId { get; init; }

    internal EventSubPredictionEvent? ToDomain(string subscriptionType, string messageId)
    {
        var status = subscriptionType switch
        {
            "channel.prediction.begin" or "channel.prediction.progress" => "active",
            "channel.prediction.lock" => "locked",
            "channel.prediction.end" when Status is "resolved" or "canceled" => Status,
            _ => null,
        };
        return status is null
            ? null
            : new(
                BroadcasterUserId,
                BroadcasterUserLogin,
                Id,
                Title,
                Outcomes.Select(x => x.ToDomain()).ToArray(),
                status,
                StartedAt,
                LocksAt ?? LockedAt,
                EndedAt,
                WinningOutcomeId,
                messageId
            );
    }

    internal sealed record Outcome
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; init; } = string.Empty;

        [JsonPropertyName("users")]
        public int Users { get; init; }

        [JsonPropertyName("channel_points")]
        public int ChannelPoints { get; init; }

        [JsonPropertyName("top_predictors")]
        public IReadOnlyList<TopPredictor>? TopPredictors { get; init; }

        internal EventSubPredictionOutcome ToDomain()
        {
            return new(
                Id,
                Title,
                Color,
                Users,
                ChannelPoints,
                (TopPredictors ?? []).Select(x => x.ToDomain()).ToArray()
            );
        }
    }

    internal sealed record TopPredictor
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("user_login")]
        public string UserLogin { get; init; } = string.Empty;

        [JsonPropertyName("user_name")]
        public string UserName { get; init; } = string.Empty;

        [JsonPropertyName("channel_points_used")]
        public int ChannelPointsUsed { get; init; }

        [JsonPropertyName("channel_points_won")]
        public int? ChannelPointsWon { get; init; }

        internal EventSubPredictionTopPredictor ToDomain()
        {
            return new(UserId, UserLogin, UserName, ChannelPointsUsed, ChannelPointsWon);
        }
    }
}
