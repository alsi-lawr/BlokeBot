namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome?> CancelAndDrainAsync(
        PluginLifecycleState state,
        PluginRuntimeSlot? previous,
        CancellationToken cancellationToken
    )
    {
        var priorFence = state.ActiveRuntime?.Fence ?? previous?.Entry.Fence;
        if (priorFence is null && previous is null)
        {
            return null;
        }

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
                ? await FaultAsync(
                    state,
                    state.Phase,
                    PluginLifecycleFailureCode.CancellationFailed,
                    cancellationFailure.Detail,
                    cancellationToken
                )
            : drained ? null
            : await FaultAsync(
                state,
                state.Phase,
                PluginLifecycleFailureCode.DrainTimedOut,
                SafeDetail("Plugin callbacks exceeded the drain bound."),
                cancellationToken
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
        var faulted = Applied(
            PluginLifecycleStateMachine.Fault(state, failedPhase, code, detail, Now())
        );
        var written = await _store.WriteAsync(state, faulted, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        faulted = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        var previous = _snapshots.Publish(faulted, worker: null);
        if (previous?.Worker is not null)
        {
            await previous.Worker.DisposeAsync();
        }

        return Failed(faulted);
    }

    private static PluginLifecycleSafeDetail? SafeDetail(string value) =>
        PluginLifecycleSafeDetail.TryCreate(value, out var detail) ? detail : null;
}
