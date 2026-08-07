namespace BlokeBot.Twitch.Runtime;

public interface IPredictionEventObserver
{
    Task PredictionReceivedAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellationToken
    );
}

public sealed record EventSubPredictionEvent(
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string PredictionId,
    string Title,
    IReadOnlyList<EventSubPredictionOutcome> Outcomes,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LocksAt,
    DateTimeOffset? EndedAt,
    string? WinningOutcomeId,
    string MessageId,
    EventSubPredictionStage Stage
)
{
    public HelixPrediction ToHelix() =>
        new(
            PredictionId,
            BroadcasterUserId,
            Title,
            Outcomes
                .Select(static x => new HelixPredictionOutcome(
                    x.Id,
                    x.Title,
                    x.Color,
                    x.Users,
                    x.ChannelPoints,
                    x.TopPredictors.Select(static p => new HelixPredictionTopPredictor(
                            p.UserId,
                            p.UserLogin,
                            p.UserName,
                            p.ChannelPointsUsed,
                            p.ChannelPointsWon
                        ))
                        .ToArray()
                ))
                .ToArray(),
            Status switch
            {
                "active" => HelixPredictionStatus.Active,
                "locked" => HelixPredictionStatus.Locked,
                "resolved" => HelixPredictionStatus.Resolved,
                "canceled" => HelixPredictionStatus.Canceled,
                _ => HelixPredictionStatus.Unknown,
            },
            CreatedAt,
            LocksAt,
            EndedAt,
            WinningOutcomeId
        );
}

public enum EventSubPredictionStage
{
    Begin,
    Progress,
    Lock,
    End,
}

public sealed record EventSubPredictionOutcome(
    string Id,
    string Title,
    string Color,
    int Users,
    int ChannelPoints,
    IReadOnlyList<EventSubPredictionTopPredictor> TopPredictors
);

public sealed record EventSubPredictionTopPredictor(
    string UserId,
    string UserLogin,
    string UserName,
    int ChannelPointsUsed,
    int? ChannelPointsWon
);
