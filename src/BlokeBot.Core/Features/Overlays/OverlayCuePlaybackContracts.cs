using System.Collections.Immutable;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

public enum OverlayCueAdmissionOrigin
{
    OwnerPreview,
    Command,
    Automation,
}

public sealed record OverlayCueSafeContext(string ViewerLogin, string ViewerDisplayName)
{
    public static OverlayCueSafeContext Empty { get; } = new(string.Empty, string.Empty);
}

public sealed record OverlayCueAdmissionRequest(
    int HostId,
    Guid TargetOverlayId,
    Guid CueId,
    OverlayCueQueuePolicy QueuePolicy,
    OverlayCueAdmissionOrigin Origin,
    OverlayCueSafeContext Context
);

public abstract record OverlayCueAdmissionOutcome
{
    private OverlayCueAdmissionOutcome() { }

    public sealed record Running(Guid RunId) : OverlayCueAdmissionOutcome;

    public sealed record Queued(Guid RunId) : OverlayCueAdmissionOutcome;

    public sealed record Missing : OverlayCueAdmissionOutcome;

    public sealed record Disabled : OverlayCueAdmissionOutcome;

    public sealed record Disconnected(Guid RunId, DateTimeOffset ExpiresAtUtc)
        : OverlayCueAdmissionOutcome;

    public sealed record QueueRejected : OverlayCueAdmissionOutcome;

    public sealed record ParentDisabledOrCancelled : OverlayCueAdmissionOutcome;

    public sealed record Expired : OverlayCueAdmissionOutcome;
}

public sealed record OverlayCueAdmissionCatalog(
    ImmutableArray<OverlayCueTargetChoice> Targets,
    ImmutableArray<OverlayCueChoice> Cues
);

public sealed record OverlayCueTargetChoice(Guid Id, string Name);

public sealed record OverlayCueChoice(
    Guid Id,
    string Name,
    OverlayCueQueuePolicy DefaultQueuePolicy
);

internal sealed record OverlayCuePlaybackPlan(
    Guid RunId,
    int HostId,
    Guid TargetOverlayId,
    Guid CueId,
    long CueRevision,
    int DurationMilliseconds,
    OverlayCueAdmissionOrigin Origin,
    OverlayCueSafeContext Context,
    ImmutableArray<OverlayCuePlaybackLayer> Layers
);

internal abstract record OverlayCuePlaybackLayer
{
    private OverlayCuePlaybackLayer() { }

    public required int StartOffsetMilliseconds { get; init; }

    public required int DurationMilliseconds { get; init; }

    public required int ZIndex { get; init; }

    public required OverlayCueRectangle Rectangle { get; init; }

    internal sealed record UploadedMedia : OverlayCuePlaybackLayer
    {
        public required Guid AssetId { get; init; }

        public required int ContentRevision { get; init; }

        public required string ContentType { get; init; }

        public required decimal Volume { get; init; }

        public required OverlayCueFitMode Fit { get; init; }
    }

    internal sealed record RemoteMedia : OverlayCuePlaybackLayer
    {
        public required Uri Url { get; init; }

        public required OverlayCueMediaKind MediaKind { get; init; }

        public required decimal Volume { get; init; }

        public required OverlayCueFitMode Fit { get; init; }
    }

    internal sealed record ExternalWeb : OverlayCuePlaybackLayer
    {
        public required Uri Url { get; init; }
    }
}

internal interface IOverlayCueTransport
{
    void Start(ResolvedOverlayInstance target, OverlayCuePlaybackPlan plan);

    void Stop(ResolvedOverlayInstance target, Guid runId);
}
