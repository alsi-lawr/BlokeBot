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

public sealed class PluginRuntimeSnapshotRegistry
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

    internal PluginRuntimeSlot? Publish(
        PluginLifecycleState state,
        IPluginLifecycleWorkerSession? worker
    )
    {
        PluginRuntimeSlot? previous;
        TaskCompletionSource<PluginLifecycleChangeVersion> change;
        PluginLifecycleChangeVersion version;
        lock (_sync)
        {
            _ = _current.Slots.TryGetValue(state.PluginId, out previous);
            var slot = new PluginRuntimeSlot(
                new(
                    state.SelectedInstallation,
                    state.Phase,
                    state.OperationId,
                    state.SelectedGeneration,
                    worker?.Mode
                ),
                new(),
                worker
            );
            Volatile.Write(
                ref _current,
                new PluginRuntimeSnapshot(_current.Slots.SetItem(state.PluginId, slot))
            );
            version = new(++_changeVersion);
            change = _change;
            _change = NewChangeCompletion();
        }

        _ = change.TrySetResult(version);
        return previous;
    }

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

    internal PluginRuntimeSlot? Remove(PluginId pluginId)
    {
        PluginRuntimeSlot? previous;
        TaskCompletionSource<PluginLifecycleChangeVersion> change;
        PluginLifecycleChangeVersion version;
        lock (_sync)
        {
            _ = _current.Slots.TryGetValue(pluginId, out previous);
            Volatile.Write(
                ref _current,
                new PluginRuntimeSnapshot(_current.Slots.Remove(pluginId))
            );
            version = new(++_changeVersion);
            change = _change;
            _change = NewChangeCompletion();
        }

        _ = change.TrySetResult(version);
        return previous;
    }

    internal void Restore(PluginId pluginId, PluginRuntimeSlot? previous)
    {
        TaskCompletionSource<PluginLifecycleChangeVersion> change;
        PluginLifecycleChangeVersion version;
        lock (_sync)
        {
            var slots = previous is null
                ? _current.Slots.Remove(pluginId)
                : _current.Slots.SetItem(pluginId, previous);
            Volatile.Write(ref _current, new PluginRuntimeSnapshot(slots));
            version = new(++_changeVersion);
            change = _change;
            _change = NewChangeCompletion();
        }

        _ = change.TrySetResult(version);
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
