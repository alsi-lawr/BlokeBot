namespace BlokeBot.Core.Features.Overlays;

public enum OverlayCueLifecycleKind
{
    Queued,
    Started,
    Finished,
    Interrupted,
}

public enum OverlayCueLifecycleOutcome
{
    Queued,
    QueuedDisconnected,
    ServerStartedUnconfirmed,
    TimeDerivedEndUnconfirmed,
    BrowserReportedEndUnverified,
    QueueExpiredUnavailable,
    CancelledOrTargetUnavailable,
    CancelledWhileQueued,
}

public sealed record OverlayCueLifecycleNotice(
    int HostId,
    Guid CueId,
    Guid RunId,
    Guid TargetId,
    OverlayCueLifecycleKind Kind,
    OverlayCueLifecycleOutcome Outcome,
    DateTimeOffset AtUtc
);

public interface IOverlayCueLifecycleObserver
{
    Task CueChangedAsync(OverlayCueLifecycleNotice notice, CancellationToken cancellationToken);
}
