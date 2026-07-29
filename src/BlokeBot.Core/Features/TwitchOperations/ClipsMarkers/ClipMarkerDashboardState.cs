namespace BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;

public sealed record ClipMarkerDashboardState(
    ClipMarkerAuthorizationReadiness Authorization,
    IReadOnlyList<ClipView> PendingClips,
    IReadOnlyList<ClipView> Results,
    IReadOnlyList<StreamMarkerView> Markers
);

public readonly record struct ClipAttemptReference(int Value);

public readonly record struct StreamMarkerAttemptReference(int Value);

public sealed record ClipView(
    ClipAttemptReference Attempt,
    string Status,
    string? ProviderClipId,
    string? EditUrl,
    string? FinalUrl,
    string? CreatorLogin,
    string? VideoId,
    string? FailureReason,
    DateTime RequestedAtUtc,
    DateTime? ResolvedAtUtc
);

public sealed record StreamMarkerView(
    StreamMarkerAttemptReference Attempt,
    string Status,
    string? ProviderMarkerId,
    string Description,
    int PositionSeconds,
    string? MarkerUrl,
    string? VideoId,
    string? FailureReason,
    DateTime CreatedAtUtc
);

public abstract record ClipMarkerAuthorizationReadiness
{
    private ClipMarkerAuthorizationReadiness() { }

    public sealed record Disabled : ClipMarkerAuthorizationReadiness;

    public sealed record Ready : ClipMarkerAuthorizationReadiness;

    public sealed record NeedsBroadcasterAuthorization(string Message)
        : ClipMarkerAuthorizationReadiness
    {
        public string ReauthorizationUrl => "/oauth/broadcaster/start";
    }
}
