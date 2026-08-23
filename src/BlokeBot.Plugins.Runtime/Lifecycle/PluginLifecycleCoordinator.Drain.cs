using Microsoft.Extensions.Logging;

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
        PluginRuntimeStopOutcome.Failed? failure = null;
        try
        {
            var cancellation = priorFence is null
                ? new PluginLifecycleOwnerOutcome.Succeeded()
                : await _pendingWork.CancelAsync(state.PluginId, priorFence, cancellationToken);
            if (cancellation is PluginLifecycleOwnerOutcome.Failed cancellationFailure)
            {
                failure = new(
                    PluginLifecycleFailureCode.CancellationFailed,
                    cancellationFailure.Detail
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Plugin pending-work cancellation failed for {PluginId}.",
                state.PluginId.Value
            );
            failure = new(
                PluginLifecycleFailureCode.CancellationFailed,
                SafeDetail("Plugin pending-work cancellation failed.")
            );
        }

        var drained = await _snapshots.DrainAsync(
            previous,
            _options.DrainTimeout,
            _timeProvider,
            cancellationToken
        );
        if (!drained && failure is null)
        {
            failure = new(
                PluginLifecycleFailureCode.DrainTimedOut,
                SafeDetail("Plugin callbacks exceeded the drain bound.")
            );
        }

        if (previous?.Worker is { } worker)
        {
            try
            {
                await worker.DisposeAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Plugin worker disposal failed for {PluginId}.",
                    state.PluginId.Value
                );
                failure = new(
                    PluginLifecycleFailureCode.WorkerDisposalFailed,
                    SafeDetail("The plugin worker could not be terminated cleanly.")
                );
            }
        }

        return failure is null ? new PluginRuntimeStopOutcome.Succeeded() : failure;
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

        var intent = Applied(
            PluginLifecycleStateMachine.BeginFaultShutdown(state, code, detail, Now())
        );
        var publication = _snapshots.StopAdmission(intent);
        PluginLifecycleStoreWriteOutcome written;
        try
        {
            written = await _store.WriteAsync(state, intent, cancellationToken);
        }
        catch
        {
            await ReconcileTerminatedCheckpointExceptionAsync(intent, publication.Ownership);
            throw;
        }

        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return await SettleAndPublishConflictAsync(
                intent,
                publication.Ownership,
                conflict.Current
            );
        }

        intent = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        return await CompleteFaultShutdownAsync(intent, publication.Ownership, cancellationToken);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> CompleteFaultShutdownAsync(
        PluginLifecycleState intent,
        PluginRuntimeSlot? previous,
        CancellationToken cancellationToken
    )
    {
        var stopped = await StopRuntimeAsync(intent, previous, cancellationToken);
        var failed = stopped as PluginRuntimeStopOutcome.Failed;
        var completed = Applied(
            PluginLifecycleStateMachine.CompleteFaultShutdown(
                intent,
                failed?.Code,
                failed?.Detail,
                Now()
            )
        );
        var written = await _store.WriteAsync(intent, completed, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return PublishConflict(intent.PluginId, conflict.Current);
        }

        completed = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(completed, worker: null);
        return Failed(completed);
    }

    private async ValueTask RecoverFaultShutdownAsync(
        PluginLifecycleState intent,
        CancellationToken cancellationToken
    )
    {
        var previous = _snapshots.StopAdmission(intent).Ownership;
        _ = await CompleteFaultShutdownAsync(intent, previous, cancellationToken);
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
            return PublishConflict(state.PluginId, conflict.Current);
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
