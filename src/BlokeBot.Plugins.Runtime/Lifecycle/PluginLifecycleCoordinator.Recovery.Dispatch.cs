namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask RecoverAsync(
        PluginLifecycleState state,
        CancellationToken cancellationToken
    )
    {
        switch (state.Phase)
        {
            case PluginLifecyclePhase.Preparing:
                await RecoverPreparationAsync(state, cancellationToken);
                break;
            case PluginLifecyclePhase.Migrating:
                await RecoverMigrationAsync(
                    state,
                    _snapshots.StopAdmission(state).Ownership,
                    cancellationToken
                );
                break;
            case PluginLifecyclePhase.Activating:
                await RecoverActivationAsync(
                    state,
                    _snapshots.StopAdmission(state).Ownership,
                    cancellationToken
                );
                break;
            case PluginLifecyclePhase.Active:
                await RecoverActiveAsync(state, cancellationToken);
                break;
            case PluginLifecyclePhase.Draining:
                await RecoverDrainAsync(state, cancellationToken);
                break;
            case PluginLifecyclePhase.Removing:
                _ = _snapshots.Publish(state, worker: null);
                _ = await CompleteRemovalAsync(state, cancellationToken);
                break;
            case PluginLifecyclePhase.Purging:
                _ = _snapshots.Publish(state, worker: null);
                _ = await CompletePurgeAsync(state, cancellationToken);
                break;
            case PluginLifecyclePhase.Removed:
                _ = _snapshots.Publish(state, worker: null);
                break;
            case PluginLifecyclePhase.Faulted:
                if (state.ActiveRuntime is null)
                {
                    _ = _snapshots.Publish(state, worker: null);
                }
                else
                {
                    await RecoverFaultShutdownAsync(state, cancellationToken);
                }

                break;
        }
    }
}
