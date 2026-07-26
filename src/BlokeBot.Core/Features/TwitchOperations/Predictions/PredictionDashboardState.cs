namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed record PredictionDashboardState(
    PredictionAuthorizationReadiness Authorization,
    PredictionView? ActivePrediction,
    IReadOnlyList<PredictionTemplateView> Templates,
    IReadOnlyList<PredictionView> Results
);

public sealed record PredictionTemplateView(
    int Id,
    string Title,
    IReadOnlyList<string> Outcomes,
    int PredictionWindowSeconds
);

public sealed record PredictionView(
    string ProviderPredictionId,
    string Title,
    IReadOnlyList<PredictionOutcomeView> Outcomes,
    string Status,
    bool IsExternallyStarted,
    DateTime CreatedAtUtc,
    DateTime? LocksAtUtc,
    DateTime? EndedAtUtc
);

public sealed record PredictionOutcomeView(
    string Id,
    string Title,
    string Color,
    int Users,
    int ChannelPoints,
    IReadOnlyList<PredictionTopPredictorView> TopPredictors
);

public sealed record PredictionTopPredictorView(
    string UserLogin,
    string UserName,
    int ChannelPointsUsed,
    int? ChannelPointsWon
);

public abstract record PredictionAuthorizationReadiness
{
    private PredictionAuthorizationReadiness() { }

    public sealed record Ready : PredictionAuthorizationReadiness;

    public sealed record Ineligible(string Message) : PredictionAuthorizationReadiness;

    public sealed record Unavailable(string Message) : PredictionAuthorizationReadiness;

    public sealed record NeedsBroadcasterAuthorization(string Message)
        : PredictionAuthorizationReadiness
    {
        public string ReauthorizationUrl => "/oauth/broadcaster/start";
    }
}
