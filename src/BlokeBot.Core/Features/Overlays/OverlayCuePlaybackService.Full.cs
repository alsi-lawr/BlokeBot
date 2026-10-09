using System.Collections.Immutable;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCuePlaybackService
{
    internal static bool HasCuePlayer(FullOverlayDocument document) =>
        document.Widgets.Any(widget => widget.Kind == new FullOverlayWidgetKind("cue-player"));

    private static async Task<OverlayCueTarget.Full?> ResolveFullTargetAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid overlayId,
        CancellationToken ct
    )
    {
        var selected = await (
            from overlay in db.FullOverlays.AsNoTracking()
            join publication in db.FullOverlayPublications.AsNoTracking()
                on new { OverlayId = overlay.Id, Version = overlay.PublishedVersion } equals new
                {
                    publication.OverlayId,
                    Version = (long?)publication.Version,
                }
            where overlay.HostId == hostId && overlay.PublicId == overlayId && !overlay.IsArchived
            select new { overlay.PublicationSequence, publication.DocumentJson }
        ).SingleOrDefaultAsync(ct);
        return
            selected is not null
            && HasCuePlayer(FullOverlayDocuments.Deserialize(selected.DocumentJson))
            ? new(hostId, overlayId, new(selected.PublicationSequence))
            : null;
    }

    internal ImmutableArray<OverlayCuePlaybackPlan> ProjectFullPlans(PublishedFullOverlay selected)
    {
        if (!_targets.TryGetValue(new(selected.HostId, selected.OverlayId), out var state))
        {
            return [];
        }
        lock (state.Gate)
        {
            return state
                .Active.Values.Where(run =>
                    run.Target is OverlayCueTarget.Full full
                    && full.Generation == selected.Generation
                )
                .Select(RemainingPlan)
                .ToImmutableArray();
        }
    }

    private OverlayCuePlaybackPlan RemainingPlan(AdmittedRun run)
    {
        var elapsed = run.StartedAtUtc is { } started
            ? (int)
                Math.Clamp(
                    (timeProvider.GetUtcNow() - started).TotalMilliseconds,
                    0,
                    run.Plan.DurationMilliseconds
                )
            : 0;
        return run.Plan with
        {
            DurationMilliseconds = Math.Max(1, run.Plan.DurationMilliseconds - elapsed),
            Layers = run
                .Plan.Layers.Where(layer =>
                    layer.StartOffsetMilliseconds + layer.DurationMilliseconds > elapsed
                )
                .Select(layer =>
                    layer with
                    {
                        StartOffsetMilliseconds = Math.Max(
                            0,
                            layer.StartOffsetMilliseconds - elapsed
                        ),
                        DurationMilliseconds =
                            layer.DurationMilliseconds
                            - Math.Max(0, elapsed - layer.StartOffsetMilliseconds),
                    }
                )
                .ToImmutableArray(),
        };
    }

    internal async Task<ImmutableArray<OverlayCuePlaybackPlan>> ProjectSamplePlansAsync(
        int hostId,
        Guid overlayId,
        CancellationToken ct
    )
    {
        var catalog = await QueryCatalogAsync(hostId, ct);
        var cue = catalog.Cues.FirstOrDefault();
        if (cue is null)
        {
            return [];
        }
        // Resolve only: private preview never admits this plan into an authoritative run queue.
        var resolution = await ResolvePlanAsync(
            new(
                hostId,
                overlayId,
                cue.Id,
                cue.DefaultQueuePolicy,
                OverlayCueAdmissionOrigin.OwnerPreview,
                OverlayCueSafeContext.Empty
            ),
            ct,
            new OverlayCueTarget.Full(hostId, overlayId, new(0))
        );
        return resolution is PlanResolution.Ready ready ? [ready.Plan] : [];
    }

    private async Task<ReferenceResolution> ResolvePreviewReferencesAsync(
        BlokeBotDbContext db,
        OverlayCueAdmissionRequest request,
        OverlayCueTarget target,
        CancellationToken ct
    )
    {
        if (!await ParentEnabledAsync(request.HostId, ct))
        {
            return new ReferenceResolution.Disabled(OverlayCueReferencePart.Parent);
        }
        var cue = await db
            .OverlayCues.AsNoTracking()
            .SingleOrDefaultAsync(
                cue =>
                    cue.HostId == request.HostId && cue.PublicId == request.CueId && cue.IsEnabled,
                ct
            );
        return cue is null
            ? new ReferenceResolution.Missing(OverlayCueReferencePart.Cue)
            : new ReferenceResolution.Available(target, cue);
    }

    internal Task<OverlayCueAdmissionOutcome> CompleteFullAsync(
        int hostId,
        Guid overlayId,
        FullOverlayGeneration generation,
        Guid runId,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!_targets.TryGetValue(new(hostId, overlayId), out var state))
        {
            return Task.FromResult<OverlayCueAdmissionOutcome>(
                new OverlayCueAdmissionOutcome.Missing()
            );
        }
        lock (state.Gate)
        {
            return
                state.Active.TryGetValue(runId, out var run)
                && run.Target is OverlayCueTarget.Full full
                && full.Generation == generation
                ? CompleteAsync(hostId, overlayId, runId, ct)
                : Task.FromResult<OverlayCueAdmissionOutcome>(
                    new OverlayCueAdmissionOutcome.Missing()
                );
        }
    }
}
