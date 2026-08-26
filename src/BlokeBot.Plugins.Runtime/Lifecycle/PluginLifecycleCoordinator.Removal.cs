using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome> RemoveAsyncCore(
        PluginId pluginId,
        PluginLifecycleOperationId operationId,
        CancellationToken cancellationToken
    )
    {
        await using var lease = await _serialization.AcquireAsync(pluginId, cancellationToken);
        var current = await _store.LoadAsync(pluginId, cancellationToken);
        if (current is null)
        {
            return new PluginLifecycleCommandOutcome.Rejected(
                PluginLifecycleCommandRejectionCode.NotFound,
                null
            );
        }

        var transition = PluginLifecycleStateMachine.BeginRemoval(current, operationId, Now());
        if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
        {
            return Rejected(rejected.Code, current);
        }

        var started = Applied(transition);
        var publication = _snapshots.StopAdmission(started);
        var checkpoint = await WriteCheckpointAsync(
            current,
            started,
            publication,
            PluginCheckpointRollbackPolicy.RestoreLiveOriginal,
            cancellationToken
        );
        if (checkpoint is PluginCheckpointWriteOutcome.Rejected checkpointRejected)
        {
            return checkpointRejected.Outcome;
        }

        var committed = (PluginCheckpointWriteOutcome.Committed)checkpoint;
        current = committed.State;
        var continuationToken =
            committed.Continuation == PluginCheckpointContinuation.LifecycleOwned
                ? CancellationToken.None
                : cancellationToken;
        if (current.Phase == PluginLifecyclePhase.Draining)
        {
            var drain = await CancelDrainAndCheckpointAsync(
                current,
                publication.Ownership,
                continuationToken
            );
            if (drain is PluginRuntimeDrainOutcome.Failed drainFailure)
            {
                return drainFailure.Outcome;
            }

            current = ((PluginRuntimeDrainOutcome.Ready)drain).State;
            var drained = Applied(PluginLifecycleStateMachine.DrainSucceeded(current, Now()));
            var written = await _store.WriteAsync(current, drained, continuationToken);
            if (written is PluginLifecycleStoreWriteOutcome.Conflict drainConflict)
            {
                return Conflict(drainConflict.Current);
            }

            current = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
            _ = _snapshots.Publish(current, worker: null);
        }

        return await CompleteRemovalAsync(current, continuationToken);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> CompleteRemovalAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        foreach (var owner in _removalOwners)
        {
            PluginLifecycleOwnerOutcome outcome;
            try
            {
                outcome = await owner.RemoveAsync(
                    new(state.PluginId, state.SelectedFence),
                    cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Plugin removal owner {OwnerType} failed for {PluginId}.",
                    owner.GetType().Name,
                    state.PluginId.Value
                );
                outcome = new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Failed,
                    SafeDetail("A plugin removal data owner failed.")
                );
            }

            if (outcome is PluginLifecycleOwnerOutcome.Failed failed)
            {
                return await FaultAsync(
                    state,
                    PluginLifecyclePhase.Removing,
                    PluginLifecycleFailureCode.RemovalFailed,
                    failed.Detail,
                    cancellationToken
                );
            }
        }

        var removedOutcome = PluginLifecycleOutcome.Progress(
            PluginLifecycleOutcomeCode.Removed,
            Now()
        );
        var persisted = await _store.CompleteRemovalAsync(state, removedOutcome, cancellationToken);
        if (persisted is PluginLifecycleStoreRemovalOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        _ = _snapshots.Remove(state.PluginId);
        return new PluginLifecycleCommandOutcome.Removed(state.PluginId);
    }
}
