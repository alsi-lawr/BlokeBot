namespace BlokeBot.Core.Features.Overlays;

public enum OverlayCueLifecycleKind
{
    Queued,
    Started,
    Finished,
    Interrupted,
}

public sealed record OverlayCueLifecycleNotice(
    int HostId,
    Guid CueId,
    Guid RunId,
    Guid TargetId,
    OverlayCueLifecycleKind Kind,
    string Outcome,
    DateTimeOffset AtUtc
);

public interface IOverlayCueLifecycleObserver
{
    Task CueChangedAsync(OverlayCueLifecycleNotice notice, CancellationToken cancellationToken);
}
