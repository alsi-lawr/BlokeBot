using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public interface IPluginRuntimeSnapshotProvider
{
    PluginRuntimeSnapshot Current { get; }

    PluginAdmissionOutcome Admit(
        PluginId pluginId,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    );

    PluginFenceOutcome ValidateCallbackCompletion(PluginId pluginId, PluginLifecycleFence fence);

    PluginFenceOutcome ValidateWorkerResult(PluginId pluginId, PluginLifecycleFence fence);

    PluginFenceOutcome ValidateCancellation(PluginId pluginId, PluginLifecycleFence fence);

    PluginAdmissionOutcome AdmitDurableRun(
        PluginId pluginId,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    );
}

public sealed record PluginLifecycleChangeVersion(long Value);

public interface IPluginLifecycleChangeNotifier
{
    PluginLifecycleChangeVersion CurrentVersion { get; }

    ValueTask<PluginLifecycleChangeVersion> WaitForChangeAsync(
        PluginLifecycleChangeVersion observed,
        CancellationToken cancellationToken
    );
}

public sealed partial class PluginRuntimeSnapshotRegistry
    : IPluginRuntimeSnapshotProvider,
        IPluginLifecycleChangeNotifier
{
    private readonly object _sync = new();
    private PluginRuntimeSnapshot _current = PluginRuntimeSnapshot.Empty;
    private TaskCompletionSource<PluginLifecycleChangeVersion> _change = NewChangeCompletion();
    private long _changeVersion;

    public PluginRuntimeSnapshot Current => Volatile.Read(ref _current);

    public PluginLifecycleChangeVersion CurrentVersion
    {
        get
        {
            lock (_sync)
            {
                return new(_changeVersion);
            }
        }
    }

    public ValueTask<PluginLifecycleChangeVersion> WaitForChangeAsync(
        PluginLifecycleChangeVersion observed,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            return observed.Value != _changeVersion
                ? ValueTask.FromResult(new PluginLifecycleChangeVersion(_changeVersion))
                : new(_change.Task.WaitAsync(cancellationToken));
        }
    }

    public PluginAdmissionOutcome Admit(
        PluginId pluginId,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    )
    {
        lock (_sync)
        {
            if (!_current.Slots.TryGetValue(pluginId, out var slot))
            {
                return Rejected(PluginAdmissionRejectionCode.Missing);
            }

            var rejection = Rejection(slot.Entry, expected, readiness);
            if (rejection is not null)
            {
                return Rejected(rejection.Value);
            }

            slot.Tracker.Admit();
            return new PluginAdmissionOutcome.Admitted(new(slot.Entry, slot.Tracker));
        }
    }

    public PluginAdmissionOutcome AdmitDurableRun(
        PluginId pluginId,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    ) => Admit(pluginId, expected, readiness);

    public PluginFenceOutcome ValidateCallbackCompletion(
        PluginId pluginId,
        PluginLifecycleFence fence
    ) => Validate(pluginId, fence);

    public PluginFenceOutcome ValidateWorkerResult(PluginId pluginId, PluginLifecycleFence fence) =>
        Validate(pluginId, fence);

    public PluginFenceOutcome ValidateCancellation(PluginId pluginId, PluginLifecycleFence fence) =>
        Validate(pluginId, fence);

    internal bool IsCurrent(PluginId pluginId, PluginLifecycleFence fence, object worker)
    {
        lock (_sync)
        {
            return _current.Slots.TryGetValue(pluginId, out var slot)
                && slot.Entry.Fence == fence
                && ReferenceEquals(slot.Worker, worker);
        }
    }

    internal PluginRuntimeSlot? FindCurrent(PluginId pluginId, PluginLifecycleFence fence)
    {
        lock (_sync)
        {
            return _current.Slots.TryGetValue(pluginId, out var slot) && slot.Entry.Fence == fence
                ? slot
                : null;
        }
    }

    internal async ValueTask<bool> DrainAsync(
        PluginRuntimeSlot? slot,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        if (slot is null)
        {
            return true;
        }

        var drained = slot.Tracker.Drained();
        var bound = Task.Delay(timeout, timeProvider, CancellationToken.None);
        if (!cancellationToken.CanBeCanceled)
        {
            return await Task.WhenAny(drained, bound) == drained;
        }

        var canceled = Task.Delay(Timeout.InfiniteTimeSpan, timeProvider, cancellationToken);
        var completed = await Task.WhenAny(drained, bound, canceled);
        if (completed == canceled)
        {
            await canceled;
        }

        return completed == drained;
    }

    private PluginFenceOutcome Validate(PluginId pluginId, PluginLifecycleFence fence)
    {
        lock (_sync)
        {
            return !_current.Slots.TryGetValue(pluginId, out var slot)
                    ? new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.Missing)
                : slot.Entry.OperationId != fence.OperationId
                    ? new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.StaleOperation)
                : slot.Entry.Generation != fence.Generation
                    ? new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.StaleGeneration)
                : slot.Entry.Phase != PluginLifecyclePhase.Active
                    ? new PluginFenceOutcome.Rejected(PluginFenceRejectionCode.NotActive)
                : new PluginFenceOutcome.Current();
        }
    }

    private static PluginAdmissionRejectionCode? Rejection(
        PluginRuntimeEntry entry,
        PluginLifecycleFence expected,
        PluginFeatureAdmissionReadiness readiness
    ) =>
        entry.OperationId != expected.OperationId ? PluginAdmissionRejectionCode.StaleOperation
        : entry.Generation != expected.Generation ? PluginAdmissionRejectionCode.StaleGeneration
        : entry.Phase == PluginLifecyclePhase.Removed ? PluginAdmissionRejectionCode.Removed
        : entry.Phase == PluginLifecyclePhase.Faulted ? PluginAdmissionRejectionCode.Faulted
        : entry.Phase != PluginLifecyclePhase.Active ? PluginAdmissionRejectionCode.NotActive
        : entry.WorkerMode == PluginWorkerMode.Staging ? PluginAdmissionRejectionCode.Staging
        : entry.WorkerMode != PluginWorkerMode.Admitted ? PluginAdmissionRejectionCode.NotActive
        : readiness == PluginFeatureAdmissionReadiness.Disabled
            ? PluginAdmissionRejectionCode.Disabled
        : readiness == PluginFeatureAdmissionReadiness.NotReady
            ? PluginAdmissionRejectionCode.NotReady
        : null;

    private static PluginAdmissionOutcome.Rejected Rejected(PluginAdmissionRejectionCode code) =>
        new(code);

    private static TaskCompletionSource<PluginLifecycleChangeVersion> NewChangeCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
