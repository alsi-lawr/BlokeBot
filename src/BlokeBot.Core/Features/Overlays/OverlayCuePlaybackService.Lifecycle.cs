namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCuePlaybackService
{
    private readonly IOverlayCueLifecycleObserver[] _lifecycleObservers =
    [
        .. lifecycleObservers ?? [],
    ];

    private void RecordLifecycle(
        List<OverlayCueLifecycleNotice> notices,
        OverlayTargetIdentity identity,
        AdmittedRun run,
        OverlayCueLifecycleKind kind,
        string outcome
    )
    {
        if (
            run.Origin
            is OverlayCueAdmissionOrigin.OwnerPreview
                or OverlayCueAdmissionOrigin.OwnerTest
        )
        {
            return;
        }
        notices.Add(
            new(
                identity.HostId,
                run.Plan.CueId,
                run.Plan.RunId,
                identity.OverlayId,
                kind,
                outcome,
                timeProvider.GetUtcNow()
            )
        );
    }

    private async Task NotifyLifecycleAsync(
        IEnumerable<OverlayCueLifecycleNotice> notices,
        CancellationToken ct
    )
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }
        foreach (var notice in notices)
        {
            foreach (var observer in _lifecycleObservers)
            {
                try
                {
                    await observer.CueChangedAsync(notice, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception failure)
                {
                    logger.LogError(
                        "Cue lifecycle observer failed for host {HostId} ({FailureType}).",
                        notice.HostId,
                        failure.GetType().Name
                    );
                }
            }
        }
    }
}
