namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginRuntimeDrainOutcome> CancelDrainAndCheckpointAsync(
        PluginLifecycleState state,
        PluginRuntimeSlot? previous,
        CancellationToken cancellationToken
    )
    {
        var stopped = await StopRuntimeAsync(state, previous, cancellationToken);
        if (stopped is PluginRuntimeStopOutcome.Failed failed)
        {
            return new PluginRuntimeDrainOutcome.Failed(
                await FaultAsync(state, state.Phase, failed.Code, failed.Detail, cancellationToken)
            );
        }

        if (
            state.ActiveRuntime is null
            || state.Phase
                is not (PluginLifecyclePhase.Migrating or PluginLifecyclePhase.Activating)
        )
        {
            return new PluginRuntimeDrainOutcome.Ready(state);
        }

        var ready = Applied(PluginLifecycleStateMachine.RuntimeStopped(state, Now()));
        var written = await _store.WriteAsync(state, ready, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return new PluginRuntimeDrainOutcome.Failed(Conflict(conflict.Current));
        }

        ready = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(ready, worker: null);
        return new PluginRuntimeDrainOutcome.Ready(ready);
    }

    private async ValueTask<PluginRuntimeStopOutcome> StopRuntimeAsync(
        PluginLifecycleState state,
        PluginRuntimeSlot? previous,
        CancellationToken cancellationToken
    )
    {
        var priorFence = state.ActiveRuntime?.Fence ?? previous?.Entry.Fence;
        var cancellation = priorFence is null
            ? new PluginLifecycleOwnerOutcome.Succeeded()
            : await _pendingWork.CancelAsync(state.PluginId, priorFence, cancellationToken);
        var drained = await _snapshots.DrainAsync(
            previous,
            _options.DrainTimeout,
            _timeProvider,
            cancellationToken
        );
        if (previous?.Worker is { } worker)
        {
            await worker.DisposeAsync();
        }

        return cancellation is PluginLifecycleOwnerOutcome.Failed cancellationFailure
                ? new PluginRuntimeStopOutcome.Failed(
                    PluginLifecycleFailureCode.CancellationFailed,
                    cancellationFailure.Detail
                )
            : drained ? new PluginRuntimeStopOutcome.Succeeded()
            : new PluginRuntimeStopOutcome.Failed(
                PluginLifecycleFailureCode.DrainTimedOut,
                SafeDetail("Plugin callbacks exceeded the drain bound.")
            );
    }

    private async ValueTask<PluginLifecycleCommandOutcome> FaultAsync(
        PluginLifecycleState state,
        PluginLifecyclePhase failedPhase,
        PluginLifecycleFailureCode code,
        PluginLifecycleSafeDetail? detail,
        CancellationToken cancellationToken
    )
    {
        if (state.Phase != PluginLifecyclePhase.Active)
        {
            return await PersistFaultAsync(state, failedPhase, code, detail, cancellationToken);
        }

        var pending = Applied(
            PluginLifecycleStateMachine.Fault(state, failedPhase, code, detail, Now())
        );
        var previous = _snapshots.Publish(pending, worker: null);
        var stopped = await StopRuntimeAsync(state, previous, cancellationToken);
        if (stopped is PluginRuntimeStopOutcome.Failed failed)
        {
            code = failed.Code;
            detail = failed.Detail;
        }

        return await PersistFaultAsync(state, failedPhase, code, detail, cancellationToken);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> PersistFaultAsync(
        PluginLifecycleState state,
        PluginLifecyclePhase failedPhase,
        PluginLifecycleFailureCode code,
        PluginLifecycleSafeDetail? detail,
        CancellationToken cancellationToken
    )
    {
        var faulted = Applied(
            PluginLifecycleStateMachine.Fault(state, failedPhase, code, detail, Now())
        );
        var written = await _store.WriteAsync(state, faulted, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            if (conflict.Current is null)
            {
                _ = _snapshots.Remove(state.PluginId);
            }
            else
            {
                _ = _snapshots.Publish(conflict.Current, worker: null);
            }

            return Conflict(conflict.Current);
        }

        faulted = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(faulted, worker: null);
        return Failed(faulted);
    }

    private static PluginLifecycleSafeDetail? SafeDetail(string value) =>
        PluginLifecycleSafeDetail.TryCreate(value, out var detail) ? detail : null;

    private abstract record PluginRuntimeDrainOutcome
    {
        private PluginRuntimeDrainOutcome() { }

        internal sealed record Ready(PluginLifecycleState State) : PluginRuntimeDrainOutcome;

        internal sealed record Failed(PluginLifecycleCommandOutcome Outcome)
            : PluginRuntimeDrainOutcome;
    }

    private abstract record PluginRuntimeStopOutcome
    {
        private PluginRuntimeStopOutcome() { }

        internal sealed record Succeeded : PluginRuntimeStopOutcome;

        internal sealed record Failed(
            PluginLifecycleFailureCode Code,
            PluginLifecycleSafeDetail? Detail
        ) : PluginRuntimeStopOutcome;
    }
}
