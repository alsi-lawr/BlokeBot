using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record HelixPrediction(
    string Id,
    string BroadcasterId,
    string Title,
    IReadOnlyList<HelixPredictionOutcome> Outcomes,
    HelixPredictionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LocksAt,
    DateTimeOffset? EndedAt,
    string? WinningOutcomeId
);

public sealed record HelixPredictionOutcome(
    string Id,
    string Title,
    string Color,
    int Users,
    int ChannelPoints,
    IReadOnlyList<HelixPredictionTopPredictor> TopPredictors
);

public sealed record HelixPredictionTopPredictor(
    string UserId,
    string UserLogin,
    string UserName,
    int ChannelPointsUsed,
    int? ChannelPointsWon
);

public sealed record HelixPredictionCreateRequest(
    string Title,
    IReadOnlyList<string> Outcomes,
    int PredictionWindowSeconds
);

public abstract record HelixPredictionCreateOutcome
{
    private HelixPredictionCreateOutcome() { }

    public sealed record Created(HelixPrediction Prediction) : HelixPredictionCreateOutcome;

    public sealed record ActivePredictionExists : HelixPredictionCreateOutcome;

    public sealed record Unauthorized : HelixPredictionCreateOutcome;

    public sealed record Ineligible : HelixPredictionCreateOutcome;

    public sealed record InvalidRequest : HelixPredictionCreateOutcome;

    public sealed record Unavailable : HelixPredictionCreateOutcome;
}

public abstract record HelixPredictionEligibilityOutcome
{
    private HelixPredictionEligibilityOutcome() { }

    public sealed record Eligible : HelixPredictionEligibilityOutcome;

    public sealed record Ineligible : HelixPredictionEligibilityOutcome;

    public sealed record Unauthorized : HelixPredictionEligibilityOutcome;

    public sealed record Unavailable : HelixPredictionEligibilityOutcome;
}

public abstract record HelixPredictionLookupOutcome
{
    private HelixPredictionLookupOutcome() { }

    public sealed record Found(IReadOnlyList<HelixPrediction> Predictions)
        : HelixPredictionLookupOutcome;

    public sealed record NoPrediction : HelixPredictionLookupOutcome;

    public sealed record Unauthorized : HelixPredictionLookupOutcome;

    public sealed record Ineligible : HelixPredictionLookupOutcome;

    public sealed record Unavailable : HelixPredictionLookupOutcome;
}

public abstract record HelixPredictionEndOutcome
{
    private HelixPredictionEndOutcome() { }

    public sealed record Updated(HelixPrediction Prediction) : HelixPredictionEndOutcome;

    public sealed record InvalidRequest : HelixPredictionEndOutcome;

    public sealed record Unauthorized : HelixPredictionEndOutcome;

    public sealed record Ineligible : HelixPredictionEndOutcome;

    public sealed record Unavailable : HelixPredictionEndOutcome;
}

public enum HelixPredictionEndStatus
{
    Locked,
    Resolved,
    Canceled,
}

public enum HelixPredictionStatus
{
    Active,
    Locked,
    Resolved,
    Canceled,
    Unknown,
}

internal sealed record HelixPredictionsResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixPredictionWire> Data { get; init; } = [];

    [JsonPropertyName("pagination")]
    public HelixPagination? Pagination { get; init; }
}

internal sealed record HelixPagination
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

internal sealed record HelixPredictionWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_id")]
    public string BroadcasterId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("outcomes")]
    public IReadOnlyList<HelixPredictionOutcomeWire> Outcomes { get; init; } = [];

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("locked_at")]
    public DateTimeOffset? LocksAt { get; init; }

    [JsonPropertyName("ended_at")]
    public DateTimeOffset? EndedAt { get; init; }

    [JsonPropertyName("winning_outcome_id")]
    public string? WinningOutcomeId { get; init; }

    public HelixPrediction ToDomain()
    {
        return new(
            Id,
            BroadcasterId,
            Title,
            Outcomes.Select(x => x.ToDomain()).ToArray(),
            Status switch
            {
                "ACTIVE" => HelixPredictionStatus.Active,
                "LOCKED" => HelixPredictionStatus.Locked,
                "RESOLVED" => HelixPredictionStatus.Resolved,
                "CANCELED" => HelixPredictionStatus.Canceled,
                _ => HelixPredictionStatus.Unknown,
            },
            CreatedAt,
            LocksAt,
            EndedAt,
            WinningOutcomeId
        );
    }
}

internal sealed record HelixPredictionOutcomeWire
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
    public IReadOnlyList<HelixPredictionTopPredictorWire>? TopPredictors { get; init; }

    public HelixPredictionOutcome ToDomain()
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

internal sealed record HelixPredictionTopPredictorWire
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

    public HelixPredictionTopPredictor ToDomain()
    {
        return new(UserId, UserLogin, UserName, ChannelPointsUsed, ChannelPointsWon);
    }
}
