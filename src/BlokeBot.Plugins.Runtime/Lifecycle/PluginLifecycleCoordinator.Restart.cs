using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    public async ValueTask<PluginLifecycleCommandOutcome> RestartAsync(
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

        var transition = PluginLifecycleStateMachine.BeginRestart(current, operationId, Now());
        if (transition is PluginLifecycleTransitionOutcome.Rejected rejected)
        {
            return Rejected(rejected.Code, current);
        }

        var resumed = Applied(transition);
        var written = await _store.WriteAsync(current, resumed, cancellationToken);
        if (written is PluginLifecycleStoreWriteOutcome.Conflict conflict)
        {
            return Conflict(conflict.Current);
        }

        resumed = ((PluginLifecycleStoreWriteOutcome.Written)written).State;
        _ = _snapshots.Publish(resumed, worker: null);
        return await ResumeRestartAsync(resumed, cancellationToken);
    }

    private async ValueTask<PluginLifecycleCommandOutcome> ResumeRestartAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        if (state.Phase == PluginLifecyclePhase.Draining)
        {
            await RecoverDrainAsync(state, cancellationToken);
            return await ReloadOutcomeAsync(state.PluginId, cancellationToken);
        }

        if (state.Phase == PluginLifecyclePhase.Removing)
        {
            return await CompleteRemovalAsync(state, cancellationToken);
        }

        if (state.Phase == PluginLifecyclePhase.Purging)
        {
            return await CompletePurgeAsync(state, cancellationToken);
        }

        var resolved = await _packages.ResolveAsync(state.SelectedInstallation, cancellationToken);
        return resolved is not PluginLifecyclePackageResolution.Available available
                ? await FaultAsync(
                    state,
                    state.Phase,
                    PluginLifecycleFailureCode.RecoveryPackageUnavailable,
                    SafeDetail("The selected plugin package is unavailable."),
                    cancellationToken
                )
            : state.Phase == PluginLifecyclePhase.Preparing
                ? await PrepareAndActivateAsync(state, available.Package, cancellationToken)
            : state.Phase == PluginLifecyclePhase.Migrating
                ? await MigrateAndActivateAsync(
                    state,
                    available.Package,
                    recovered: true,
                    cancellationToken
                )
            : await StartAndPublishAsync(
                state,
                available.Package,
                recovered: true,
                cancellationToken
            );
    }

    private async ValueTask<PluginLifecycleCommandOutcome> ReloadOutcomeAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var current = await _store.LoadAsync(pluginId, cancellationToken);
        if (current is null)
        {
            var tombstone = await _store.LoadTombstoneAsync(pluginId, cancellationToken);
            return tombstone is null ? Conflict(null) : Purged(tombstone);
        }

        return current.Phase == PluginLifecyclePhase.Faulted ? Failed(current) : Succeeded(current);
    }
}
