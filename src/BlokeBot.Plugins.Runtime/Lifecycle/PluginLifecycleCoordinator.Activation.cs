using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome> PrepareAndActivateAsync(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        var validation = await _workers.ValidateAsync(package, cancellationToken);
        if (validation is PluginLifecycleWorkerStartOutcome.Failed failed)
        {
            return await FailPreparationAsync(state, failed, cancellationToken);
        }

        var migrating = Applied(PluginLifecycleStateMachine.PreparationSucceeded(state, Now()));
        var publication = _snapshots.StopAdmission(migrating);
        var migrationFence = await WriteCheckpointAsync(
            state,
            migrating,
            publication,
            PluginCheckpointRollbackPolicy.RestoreLiveOriginal,
            cancellationToken
        );
        if (migrationFence is PluginCheckpointWriteOutcome.Rejected rejected)
        {
            return rejected.Outcome;
        }

        var committed = (PluginCheckpointWriteOutcome.Committed)migrationFence;
        state = committed.State;
        var continuationToken =
            committed.Continuation == PluginCheckpointContinuation.LifecycleOwned
                ? CancellationToken.None
                : cancellationToken;
        var drain = await CancelDrainAndCheckpointAsync(
            state,
            publication.Ownership,
            continuationToken
        );
        return drain is PluginRuntimeDrainOutcome.Failed drainFailure
            ? drainFailure.Outcome
            : await MigrateAndActivateAsync(
                ((PluginRuntimeDrainOutcome.Ready)drain).State,
                package,
                recovered: false,
                continuationToken
            );
    }

    private async ValueTask<PluginLifecycleCommandOutcome> MigrateAndActivateAsync(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        bool recovered,
        CancellationToken cancellationToken
    )
    {
        var migration = await RunMigrationOwnersAsync(state, cancellationToken);
        if (migration is PluginLifecycleOwnerOutcome.Failed failed)
        {
            return await FaultAsync(
                state,
                PluginLifecyclePhase.Migrating,
                PluginLifecycleFailureCode.MigrationFailed,
                failed.Detail,
                cancellationToken
            );
        }

        var activating = Applied(PluginLifecycleStateMachine.MigrationSucceeded(state, Now()));
        var written = await _store.WriteAsync(state, activating, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        state = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(state, worker: null);
        return await StartAndPublishAsync(state, package, recovered, cancellationToken);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> StartAndPublishAsync(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        bool recovered,
        CancellationToken cancellationToken
    )
    {
        var started = await _workers.StartAdmittedAsync(package, cancellationToken);
        if (started is PluginLifecycleWorkerStartOutcome.Failed failed)
        {
            return await FaultAsync(
                state,
                PluginLifecyclePhase.Activating,
                failed.Code,
                failed.Detail,
                cancellationToken
            );
        }

        var worker = ((PluginLifecycleWorkerStartOutcome.Started)started).Worker;
        var active = Applied(
            PluginLifecycleStateMachine.ActivationSucceeded(state, Now(), recovered)
        );
        var written = await _store.WriteAsync(state, active, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            await worker.DisposeAsync();
            return Conflict(conflict.Current);
        }

        active = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(active, worker);
        ObserveWorkerTermination(active, package, worker);
        return Succeeded(active);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> FailPreparationAsync(
        PluginLifecycleState state,
        PluginLifecycleWorkerStartOutcome.Failed failure,
        CancellationToken cancellationToken
    )
    {
        var transition = PluginLifecycleStateMachine.PreparationFailed(
            state,
            failure.Code,
            failure.Detail,
            Now()
        );
        var written = await _store.WriteAsync(state, Applied(transition), cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        var failed = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        if (failed.Phase == PluginLifecyclePhase.Faulted)
        {
            _ = _snapshots.Publish(failed, worker: null);
        }

        return Failed(failed);
    }

    private async ValueTask<PluginLifecycleOwnerOutcome> RunMigrationOwnersAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        foreach (var owner in _migrationOwners)
        {
            try
            {
                var outcome = await owner.MigrateAsync(
                    new(state.SelectedInstallation, state.SelectedFence),
                    cancellationToken
                );
                if (outcome is PluginLifecycleOwnerOutcome.Failed)
                {
                    return outcome;
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
                    "Plugin migration owner {OwnerType} failed for {PluginId}.",
                    owner.GetType().Name,
                    state.PluginId.Value
                );
                return new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Failed,
                    SafeDetail("A plugin migration data owner failed.")
                );
            }
        }

        return new PluginLifecycleOwnerOutcome.Succeeded();
    }

    private static PluginLifecycleState Applied(PluginLifecycleTransitionOutcome outcome) =>
        outcome is PluginLifecycleTransitionOutcome.Applied applied
            ? applied.State
            : throw new InvalidOperationException("The lifecycle transition was not legal.");
}
