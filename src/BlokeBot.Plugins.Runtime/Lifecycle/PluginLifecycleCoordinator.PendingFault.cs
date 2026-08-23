using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome?> CompletePendingFaultShutdownAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        var current = await _store.LoadAsync(pluginId, cancellationToken);
        if (current is not { Phase: PluginLifecyclePhase.Faulted, ActiveRuntime: not null })
        {
            return null;
        }

        var ownership = _snapshots.StopAdmission(current);
        var completed = await CompleteFaultShutdownAsync(current, ownership, cancellationToken);
        return completed is PluginLifecycleCommandOutcome.Rejected ? completed : null;
    }
}
