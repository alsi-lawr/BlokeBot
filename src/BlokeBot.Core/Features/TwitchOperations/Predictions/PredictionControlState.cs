namespace BlokeBot.Core.Features.TwitchOperations.Predictions;

public sealed record PredictionControlState(
    PredictionAuthorizationReadiness Authorization,
    PredictionTemplateView? Template,
    PredictionControlView? ActivePrediction
);

public sealed record PredictionControlView(
    string ProviderPredictionId,
    string Title,
    IReadOnlyList<PredictionControlOutcomeView> Outcomes,
    string Status,
    bool IsExternallyStarted,
    DateTime CreatedAtUtc,
    DateTime? LocksAtUtc
);

public sealed record PredictionControlOutcomeView(
    string Id,
    string Title,
    string Color,
    int Users,
    int ChannelPoints
);
