using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    public async ValueTask RecoverAsync(CancellationToken cancellationToken)
    {
        var states = await _store.LoadAllAsync(cancellationToken);
        foreach (var state in states)
        {
            try
            {
                await using var lease = await _serialization.AcquireAsync(
                    state.PluginId,
                    cancellationToken
                );
                var current = await _store.LoadAsync(state.PluginId, cancellationToken);
                if (current is not null)
                {
                    await RecoverAsync(current, cancellationToken);
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
                    "Plugin lifecycle recovery failed for {PluginId}.",
                    state.PluginId.Value
                );
            }
        }
    }

    private async ValueTask RecoverPreparationAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        if (state.ActiveRuntime is { } active)
        {
            var existing = _snapshots.FindCurrent(state.PluginId, active.Fence);
            if (existing?.Worker is not null)
            {
                await RecoverSelectedPreparationAsync(state, cancellationToken);
                return;
            }

            var previousPackage = await _packages.ResolveAsync(
                active.Installation,
                cancellationToken
            );
            if (previousPackage is not PluginLifecyclePackageResolution.Available previous)
            {
                _ = await FaultAsync(
                    state,
                    PluginLifecyclePhase.Preparing,
                    PluginLifecycleFailureCode.RecoveryPackageUnavailable,
                    SafeDetail("The active plugin package is unavailable during recovery."),
                    cancellationToken
                );
                return;
            }

            var started = await _workers.StartAdmittedAsync(previous.Package, cancellationToken);
            if (started is not PluginLifecycleWorkerStartOutcome.Started restored)
            {
                _ = await FaultAsync(
                    state,
                    PluginLifecyclePhase.Preparing,
                    PluginLifecycleFailureCode.WorkerStartFailed,
                    SafeDetail("The active plugin worker could not be restored."),
                    cancellationToken
                );
                return;
            }

            var activeState = state with
            {
                SelectedInstallation = active.Installation,
                OperationId = active.Fence.OperationId,
                SelectedGeneration = active.Fence.Generation,
                Phase = PluginLifecyclePhase.Active,
            };
            _ = _snapshots.Publish(activeState, restored.Worker);
            ObserveUnexpectedExit(activeState, previous.Package, restored.Worker);
        }

        await RecoverSelectedPreparationAsync(state, cancellationToken);
    }

    private async ValueTask RecoverSelectedPreparationAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        var selected = await _packages.ResolveAsync(state.SelectedInstallation, cancellationToken);
        if (selected is PluginLifecyclePackageResolution.Available available)
        {
            _ = await PrepareAndActivateAsync(state, available.Package, cancellationToken);
            return;
        }

        _ = await FailPreparationAsync(
            state,
            new PluginLifecycleWorkerStartOutcome.Failed(
                PluginLifecycleFailureCode.RecoveryPackageUnavailable,
                SafeDetail(
                    "The selected plugin package is unavailable during preparation recovery."
                )
            ),
            cancellationToken
        );
    }

    private async ValueTask RecoverMigrationAsync(
        PluginLifecycleState state,
        PluginRuntimeSlot? previous,
        CancellationToken cancellationToken
    )
    {
        var drain = await CancelDrainAndCheckpointAsync(state, previous, cancellationToken);
        if (drain is PluginRuntimeDrainOutcome.Failed)
        {
            return;
        }

        state = ((PluginRuntimeDrainOutcome.Ready)drain).State;

        var resolved = await _packages.ResolveAsync(state.SelectedInstallation, cancellationToken);
        if (resolved is PluginLifecyclePackageResolution.Available available)
        {
            _ = await MigrateAndActivateAsync(
                state,
                available.Package,
                recovered: true,
                cancellationToken
            );
            return;
        }

        _ = await FaultAsync(
            state,
            PluginLifecyclePhase.Migrating,
            PluginLifecycleFailureCode.RecoveryPackageUnavailable,
            SafeDetail("The selected plugin package is unavailable during migration recovery."),
            cancellationToken
        );
    }

    private async ValueTask RecoverActivationAsync(
        PluginLifecycleState state,
        PluginRuntimeSlot? previous,
        CancellationToken cancellationToken
    )
    {
        var drain = await CancelDrainAndCheckpointAsync(state, previous, cancellationToken);
        if (drain is PluginRuntimeDrainOutcome.Failed)
        {
            return;
        }

        state = ((PluginRuntimeDrainOutcome.Ready)drain).State;
        if (state.RestartNotBeforeUtc is { } notBefore)
        {
            await DelayUntilAsync(notBefore, cancellationToken);
        }

        var resolved = await _packages.ResolveAsync(state.SelectedInstallation, cancellationToken);
        if (resolved is PluginLifecyclePackageResolution.Available available)
        {
            _ = await StartAndPublishAsync(
                state,
                available.Package,
                recovered: true,
                cancellationToken
            );
            return;
        }

        _ = await FaultAsync(
            state,
            PluginLifecyclePhase.Activating,
            PluginLifecycleFailureCode.RecoveryPackageUnavailable,
            SafeDetail("The selected plugin package is unavailable during activation recovery."),
            cancellationToken
        );
    }

    private async ValueTask RecoverActiveAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        var resolved = await _packages.ResolveAsync(state.SelectedInstallation, cancellationToken);
        if (resolved is not PluginLifecyclePackageResolution.Available available)
        {
            _ = await FaultAsync(
                state,
                PluginLifecyclePhase.Active,
                PluginLifecycleFailureCode.RecoveryPackageUnavailable,
                SafeDetail("The active plugin package is unavailable during recovery."),
                cancellationToken
            );
            return;
        }

        var started = await _workers.StartAdmittedAsync(available.Package, cancellationToken);
        if (started is not PluginLifecycleWorkerStartOutcome.Started restored)
        {
            _ = await FaultAsync(
                state,
                PluginLifecyclePhase.Active,
                PluginLifecycleFailureCode.WorkerStartFailed,
                SafeDetail("The active plugin worker could not be recovered."),
                cancellationToken
            );
            return;
        }

        var recovered = Applied(PluginLifecycleStateMachine.ActiveRecoverySucceeded(state, Now()));
        var written = await _store.WriteAsync(state, recovered, cancellationToken);
        if (written is not PluginLifecycleStoreWriteOutcome.Written recoveredWrite)
        {
            await restored.Worker.DisposeAsync();
            return;
        }

        recovered = recoveredWrite.State;
        _ = _snapshots.Publish(recovered, restored.Worker);
        ObserveUnexpectedExit(recovered, available.Package, restored.Worker);
    }

    private async ValueTask RecoverDrainAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        var previous = _snapshots.StopAdmission(state);
        var drain = await CancelDrainAndCheckpointAsync(state, previous, cancellationToken);
        if (drain is PluginRuntimeDrainOutcome.Failed)
        {
            return;
        }

        state = ((PluginRuntimeDrainOutcome.Ready)drain).State;

        var drained = Applied(PluginLifecycleStateMachine.DrainSucceeded(state, Now()));
        var written = await _store.WriteAsync(state, drained, cancellationToken);
        if (written is not PluginLifecycleStoreWriteOutcome.Written drainWrite)
        {
            return;
        }

        drained = drainWrite.State;
        _ = _snapshots.Publish(drained, worker: null);
        if (drained.Phase == PluginLifecyclePhase.Purging)
        {
            _ = await CompletePurgeAsync(drained, cancellationToken);
        }
        else
        {
            _ = await CompleteRemovalAsync(drained, cancellationToken);
        }
    }
}
