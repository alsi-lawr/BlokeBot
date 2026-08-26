using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome> PrepareAndActivateAsync(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    ) => await PrepareAndActivateAsync(state, package, publication: null, cancellationToken);

    private async ValueTask<PluginLifecycleCommandOutcome> PrepareAndActivateAsync(
        PluginLifecycleState state,
        PluginLifecyclePackage package,
        PluginAdmissionStopPublication? publication,
        CancellationToken cancellationToken
    )
    {
        var validation = await _workers.ValidateAsync(package, cancellationToken);
        if (validation is PluginLifecycleWorkerStartOutcome.Failed failed)
        {
            return publication is null
                ? await FailPreparationAsync(state, failed, cancellationToken)
                : await FailSelectedReplacementPreparationAsync(
                    state,
                    failed,
                    publication,
                    cancellationToken
                );
        }

        var migrating = Applied(PluginLifecycleStateMachine.PreparationSucceeded(state, Now()));
        publication ??= _snapshots.StopAdmission(migrating);
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
        var migration = await RunMigrationOwnersAsync(state, package, cancellationToken);
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
        var context = new PluginLifecycleActivationContext(
            state.SelectedInstallation,
            state.SelectedFence,
            package
        );
        PluginLifecycleOwnerOutcome publication;
        try
        {
            publication = await PublishActivationAsync(context, cancellationToken);
        }
        catch
        {
            await DisposeUnpublishedWorkerAsync(worker, context.Installation.PluginId);
            throw;
        }
        if (publication is PluginLifecycleOwnerOutcome.Failed publicationFailure)
        {
            await DisposeUnpublishedWorkerAsync(worker, context.Installation.PluginId);
            return await FaultAsync(
                state,
                PluginLifecyclePhase.Activating,
                PluginLifecycleFailureCode.ActivationFailed,
                publicationFailure.Detail,
                cancellationToken
            );
        }

        var active = Applied(
            PluginLifecycleStateMachine.ActivationSucceeded(state, Now(), recovered)
        );
        PluginLifecycleStoreWriteOutcome written;
        try
        {
            written = await _store.WriteAsync(state, active, cancellationToken);
        }
        catch
        {
            var current = await _store.LoadAsync(state.PluginId, CancellationToken.None);
            if (current == active)
            {
                _ = _snapshots.Publish(active, worker);
                ObserveWorkerTermination(active, package, worker);
                return Succeeded(active);
            }

            await DisposeUnpublishedWorkerAsync(worker, context.Installation.PluginId);
            await WithdrawActivationAsync(context, CancellationToken.None);
            throw;
        }
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            if (conflict.Current == active)
            {
                _ = _snapshots.Publish(active, worker);
                ObserveWorkerTermination(active, package, worker);
                return Succeeded(active);
            }

            await DisposeUnpublishedWorkerAsync(worker, context.Installation.PluginId);
            await WithdrawActivationAsync(context, CancellationToken.None);
            return Conflict(conflict.Current);
        }

        active = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(active, worker);
        ObserveWorkerTermination(active, package, worker);
        return Succeeded(active);
    }

    private async ValueTask DisposeUnpublishedWorkerAsync(
        IPluginLifecycleWorkerSession worker,
        PluginId pluginId
    )
    {
        try
        {
            await worker.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unpublished plugin worker disposal failed for {PluginId}.",
                pluginId.Value
            );
        }
    }

    private async ValueTask<PluginLifecycleOwnerOutcome> PublishActivationAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var publisher in _activationPublishers)
        {
            try
            {
                var outcome = await publisher.PublishAsync(context, cancellationToken);
                if (outcome is PluginLifecycleOwnerOutcome.Failed)
                {
                    await WithdrawActivationAsync(context, CancellationToken.None);
                    return outcome;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await WithdrawActivationAsync(context, CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Plugin activation publisher {PublisherType} failed for {PluginId}.",
                    publisher.GetType().Name,
                    context.Installation.PluginId.Value
                );
                await WithdrawActivationAsync(context, CancellationToken.None);
                return new PluginLifecycleOwnerOutcome.Failed(
                    PluginLifecycleOwnerFailureCode.Failed,
                    SafeDetail("Plugin declarations could not be published.")
                );
            }
        }

        return new PluginLifecycleOwnerOutcome.Succeeded();
    }

    private async ValueTask WithdrawActivationAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var publisher in _activationPublishers.Reverse())
        {
            try
            {
                await publisher.WithdrawAsync(context, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Plugin activation publication rollback {PublisherType} failed for {PluginId}.",
                    publisher.GetType().Name,
                    context.Installation.PluginId.Value
                );
            }
        }
    }

    private async ValueTask<PluginLifecycleCommandOutcome> FailSelectedReplacementPreparationAsync(
        PluginLifecycleState state,
        PluginLifecycleWorkerStartOutcome.Failed failure,
        PluginAdmissionStopPublication publication,
        CancellationToken cancellationToken
    )
    {
        var migrating = Applied(PluginLifecycleStateMachine.PreparationSucceeded(state, Now()));
        var checkpoint = await WriteCheckpointAsync(
            state,
            migrating,
            publication,
            PluginCheckpointRollbackPolicy.SettleRuntime,
            cancellationToken
        );
        if (checkpoint is PluginCheckpointWriteOutcome.Rejected rejected)
        {
            return rejected.Outcome;
        }

        var committed = (PluginCheckpointWriteOutcome.Committed)checkpoint;
        var drain = await CancelDrainAndCheckpointAsync(
            committed.State,
            publication.Ownership,
            committed.Continuation == PluginCheckpointContinuation.LifecycleOwned
                ? CancellationToken.None
                : cancellationToken
        );
        if (drain is PluginRuntimeDrainOutcome.Failed drainFailure)
        {
            return drainFailure.Outcome;
        }

        var ready = ((PluginRuntimeDrainOutcome.Ready)drain).State;
        return await FaultAsync(
            ready,
            ready.Phase,
            failure.Code,
            failure.Detail,
            cancellationToken
        );
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
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    )
    {
        foreach (var owner in _migrationOwners)
        {
            try
            {
                var outcome = await owner.MigrateAsync(
                    new(state.SelectedInstallation, state.SelectedFence, package),
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
