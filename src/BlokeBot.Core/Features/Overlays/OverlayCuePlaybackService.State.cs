using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCuePlaybackService
{
    private readonly record struct OverlayTargetIdentity(int HostId, Guid OverlayId);

    private sealed class TargetState
    {
        internal object Gate { get; } = new();

        internal Dictionary<Guid, AdmittedRun> Active { get; } = [];

        internal Queue<AdmittedRun> Pending { get; } = [];

        internal HashSet<Guid> Expired { get; } = [];

        internal HashSet<Guid> Cancelled { get; } = [];
    }

    private sealed record AdmittedRun(
        ResolvedOverlayInstance Target,
        OverlayCuePlaybackPlan Plan,
        OverlayCueQueuePolicy QueuePolicy,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? StartedAtUtc = null
    );

    private abstract record PlanResolution
    {
        private PlanResolution() { }

        internal sealed record Ready(ResolvedOverlayInstance Target, OverlayCuePlaybackPlan Plan)
            : PlanResolution;

        internal sealed record Missing : PlanResolution;

        internal sealed record Disabled : PlanResolution;

        internal sealed record ParentDisabled : PlanResolution;
    }

    private abstract record ReferenceResolution
    {
        private ReferenceResolution() { }

        internal sealed record Available(OverlayInstance Target, OverlayCue Cue)
            : ReferenceResolution;

        internal sealed record Missing(OverlayCueReferencePart Part) : ReferenceResolution;

        internal sealed record Disabled(OverlayCueReferencePart Part) : ReferenceResolution;
    }
}
