using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome> RemoveOrPurgeAsync(
        PluginId pluginId,
        PluginLifecycleOperationId operationId,
        bool purge,
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

        var transition = PluginLifecycleStateMachine.BeginRemoval(
            current,
            operationId,
            purge,
            Now()
        );
        if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
        {
            return Rejected(rejected.Code, current);
        }

        var started = Applied(transition);
        var previous = _snapshots.Publish(started, worker: null);
        var written = await _store.WriteAsync(current, started, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            _snapshots.Restore(pluginId, previous);
            return Conflict(conflict.Current);
        }

        current = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        if (current.Phase == PluginLifecyclePhase.Draining)
        {
            var drainFailure = await CancelAndDrainAsync(current, previous, cancellationToken);
            if (drainFailure is not null)
            {
                return drainFailure;
            }

            var drained = Applied(PluginLifecycleStateMachine.DrainSucceeded(current, Now()));
            written = await _store.WriteAsync(current, drained, cancellationToken);
            if (written is PluginLifecycleStoreWriteOutcome.Conflict drainConflict)
            {
                return Conflict(drainConflict.Current);
            }

            current = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
            _ = _snapshots.Publish(current, worker: null);
        }

        return current.Phase == PluginLifecyclePhase.Purging
            ? await CompletePurgeAsync(current, cancellationToken)
            : await CompleteRemovalAsync(current, cancellationToken);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> CompleteRemovalAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        var removed = Applied(PluginLifecycleStateMachine.RemovalSucceeded(state, Now()));
        var written = await _store.WriteAsync(state, removed, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        removed = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(removed, worker: null);
        return Succeeded(removed);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> CompletePurgeAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        foreach (var owner in _purgeOwners)
        {
            PluginLifecycleOwnerOutcome outcome;
            try
            {
                outcome = await owner.PurgeAsync(
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
                    "Plugin purge owner {OwnerType} failed for {PluginId}.",
                    owner.GetType().Name,
                    state.PluginId.Value
                );
                outcome = new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Failed,
                    SafeDetail("A plugin purge data owner failed.")
                );
            }

            if (outcome is PluginLifecycleOwnerOutcome.Failed failed)
            {
                return await FaultAsync(
                    state,
                    PluginLifecyclePhase.Purging,
                    PluginLifecycleFailureCode.PurgeFailed,
                    failed.Detail,
                    cancellationToken
                );
            }
        }

        var purged = Applied(PluginLifecycleStateMachine.PurgeSucceeded(state, Now()));
        var written = await _store.WriteAsync(state, purged, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        purged = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(purged, worker: null);
        return Succeeded(purged);
    }
}
