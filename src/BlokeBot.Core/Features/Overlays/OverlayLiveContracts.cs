namespace BlokeBot.Core.Features.Overlays;

internal enum OverlayLivePublicationKind
{
    State,
    Test,
}

internal sealed record EmptyV1OverlayLivePayload
{
    public string OverlayType => "empty";

    public int SchemaVersion => 1;

    public EmptyV1OverlayPresentationState State { get; } = new();
}

internal sealed record EmptyV1OverlayLiveEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    internal OverlayLivePublicationKind Kind { get; init; }

    public string EventType => Kind is OverlayLivePublicationKind.Test ? "test" : "state";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public EmptyV1OverlayLivePayload Payload { get; } = new();
}

internal sealed record EmptyV1OverlayLiveBaselineEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public string EventType => "baseline";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public EmptyV1OverlayLivePayload Payload { get; } = new();
}

internal sealed record GuessingV1OverlayLivePayload
{
    public string OverlayType => "guessing";

    public int SchemaVersion => 1;

    public required int ResultDurationMilliseconds { get; init; }

    public required string Animation { get; init; }

    public required GuessingV1OverlayPresentationState State { get; init; }
}

internal sealed record GuessingV1OverlayLiveEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    internal OverlayLivePublicationKind Kind { get; init; }

    public string EventType => Kind is OverlayLivePublicationKind.Test ? "test" : "state";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required GuessingV1OverlayLivePayload Payload { get; init; }
}

internal sealed record GuessingV1OverlayLiveBaselineEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public string EventType => "baseline";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required GuessingV1OverlayLivePayload Payload { get; init; }
}

internal sealed record GiveawayV1OverlayLivePayload
{
    public string OverlayType => "giveaway";

    public int SchemaVersion => 1;

    public required int WinnerAnimationDurationMilliseconds { get; init; }

    public required string Animation { get; init; }

    public required GiveawayV1OverlayPresentationState State { get; init; }
}

internal sealed record GiveawayV1OverlayLiveEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    internal OverlayLivePublicationKind Kind { get; init; }

    public string EventType => Kind is OverlayLivePublicationKind.Test ? "test" : "state";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required GiveawayV1OverlayLivePayload Payload { get; init; }
}

internal sealed record GiveawayV1OverlayLiveBaselineEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public string EventType => "baseline";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required GiveawayV1OverlayLivePayload Payload { get; init; }
}

internal sealed record OverlayLiveControlEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}

internal sealed record CuePlayerV1OverlayLivePayload
{
    public string OverlayType => "cuePlayer";

    public int SchemaVersion => 1;

    public CuePlayerV1OverlayPresentationState State { get; } = new();
}

internal sealed record CuePlayerV1OverlayLiveBaselineEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public string EventType => "baseline";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public CuePlayerV1OverlayLivePayload Payload { get; } = new();
}

internal sealed record CuePlaybackLiveEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public string EventType => "cue";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required CuePlaybackLivePayload Payload { get; init; }
}

internal sealed record CuePlaybackStopLiveEnvelope
{
    public int ProtocolVersion => 1;

    public required Guid ServerEpoch { get; init; }

    public required long Sequence { get; init; }

    public string EventType => "cueStop";

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required Guid RunId { get; init; }
}

internal sealed record CuePlaybackLivePayload
{
    public string OverlayType => "cuePlayer";

    public int SchemaVersion => 1;

    public required Guid RunId { get; init; }

    public required int DurationMilliseconds { get; init; }

    public required IReadOnlyList<CuePlaybackLayerPayload> Layers { get; init; }
}

internal sealed record CuePlaybackLayerPayload
{
    public required string Kind { get; init; }

    public string? Url { get; init; }

    public Guid? AssetId { get; init; }

    public int? ContentRevision { get; init; }

    public string? ContentType { get; init; }

    public string? MediaKind { get; init; }

    public decimal? Volume { get; init; }

    public string? Fit { get; init; }

    public required int StartOffsetMilliseconds { get; init; }

    public required int DurationMilliseconds { get; init; }

    public required int ZIndex { get; init; }

    public required OverlayCueRectangle Rectangle { get; init; }
}

public sealed record OverlayConnectionPresence
{
    public required int ActiveConnectionCount { get; init; }

    public DateTimeOffset? MostRecentConnectedAtUtc { get; init; }

    public DateTimeOffset? MostRecentDisconnectedAtUtc { get; init; }
}

public interface IOverlayLivePublisher
{
    void PublishState(ResolvedOverlayInstance instance);

    void PublishTest(ResolvedOverlayInstance instance);
}

public interface IOverlayLivePresence
{
    OverlayConnectionPresence Read(int hostId, Guid overlayId);
}
