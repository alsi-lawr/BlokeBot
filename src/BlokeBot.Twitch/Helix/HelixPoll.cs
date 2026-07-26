using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record HelixPoll(
    string Id,
    string BroadcasterId,
    string Title,
    IReadOnlyList<HelixPollChoice> Choices,
    HelixPollStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt
);

public sealed record HelixPollChoice(string Id, string Title, int Votes, int ChannelPointsVotes);

public enum HelixPollStatus
{
    Active,
    Completed,
    Terminated,
    Archived,
    Unknown,
}

public sealed record HelixPollCreateRequest(
    string Title,
    IReadOnlyList<string> Choices,
    int DurationSeconds,
    bool ChannelPointsVotingEnabled,
    int? ChannelPointsPerVote
);

public abstract record HelixPollCreateOutcome
{
    private HelixPollCreateOutcome() { }

    public sealed record Created(HelixPoll Poll) : HelixPollCreateOutcome;

    public sealed record ActivePollExists : HelixPollCreateOutcome;

    public sealed record ProviderRejected : HelixPollCreateOutcome;
}

public enum HelixPollEndStatus
{
    Terminated,
    Archived,
}

internal sealed record HelixPollResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixPollWire> Data { get; init; } = [];
}

internal sealed record HelixPollWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_id")]
    public string BroadcasterId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("choices")]
    public IReadOnlyList<HelixPollChoiceWire> Choices { get; init; } = [];

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("ends_at")]
    public DateTimeOffset? EndsAt { get; init; }

    public HelixPoll ToDomain()
    {
        return new(
            Id,
            BroadcasterId,
            Title,
            Choices
                .Select(x => new HelixPollChoice(x.Id, x.Title, x.Votes, x.ChannelPointsVotes))
                .ToArray(),
            Status switch
            {
                "ACTIVE" => HelixPollStatus.Active,
                "COMPLETED" => HelixPollStatus.Completed,
                "TERMINATED" => HelixPollStatus.Terminated,
                "ARCHIVED" => HelixPollStatus.Archived,
                _ => HelixPollStatus.Unknown,
            },
            StartedAt,
            EndsAt
        );
    }
}

internal sealed record HelixPollChoiceWire
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
