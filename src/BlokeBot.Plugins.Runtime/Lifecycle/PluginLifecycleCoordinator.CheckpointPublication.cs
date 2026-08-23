using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginLifecycleCoordinator
{
    private async ValueTask<PluginLifecycleCommandOutcome> ReconcileCommandCheckpointConflictAsync(
        PluginLifecycleState expected,
        PluginLifecycleState stopped,
        PluginAdmissionStopPublication publication,
        PluginLifecycleState? current
    )
    {
        if (RetainsOriginalRuntime(expected, current, publication))
        {
            _snapshots.RestoreOriginal(publication);
            return Conflict(current);
        }

        return current == stopped
            ? Conflict(current)
            : await SettleAndPublishConflictAsync(stopped, publication.Ownership, current);
    }

    private async ValueTask ReconcileCommandCheckpointExceptionAsync(
        PluginLifecycleState expected,
        PluginLifecycleState stopped,
        PluginAdmissionStopPublication publication
    )
    {
        try
        {
            var current = await _store.LoadAsync(expected.PluginId, CancellationToken.None);
            if (RetainsOriginalRuntime(expected, current, publication))
            {
                _snapshots.RestoreOriginal(publication);
            }
            else if (current != stopped)
            {
                _ = await SettleAndPublishConflictAsync(stopped, publication.Ownership, current);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Plugin checkpoint exception reconciliation failed for {PluginId}.",
                expected.PluginId.Value
            );
        }
    }

    private async ValueTask ReconcileTerminatedCheckpointExceptionAsync(
        PluginLifecycleState stopped,
        PluginRuntimeSlot? ownership
    )
    {
        try
        {
            var current = await _store.LoadAsync(stopped.PluginId, CancellationToken.None);
            if (current != stopped)
            {
                _ = await SettleAndPublishConflictAsync(stopped, ownership, current);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Plugin terminated-checkpoint reconciliation failed for {PluginId}.",
                stopped.PluginId.Value
            );
        }
    }

    private async ValueTask<PluginLifecycleCommandOutcome> SettleAndPublishConflictAsync(
        PluginLifecycleState stopped,
        PluginRuntimeSlot? ownership,
        PluginLifecycleState? current
    )
    {
        _ = await StopRuntimeAsync(stopped, ownership, CancellationToken.None);
        return PublishConflict(stopped.PluginId, current);
    }

    private static bool RetainsOriginalRuntime(
        PluginLifecycleState expected,
        PluginLifecycleState? current,
        PluginAdmissionStopPublication publication
    ) =>
        current == expected
        || (
            current is { Phase: PluginLifecyclePhase.Active, ActiveRuntime: { } activeRuntime }
            && publication.Original
                is {
                    Entry.Phase: PluginLifecyclePhase.Active,
                    Entry: var entry,
                    RuntimeFence: { } runtimeFence,
                    Worker: not null,
                }
            && entry.Installation == current.SelectedInstallation
            && entry.Fence == current.SelectedFence
            && runtimeFence == entry.Fence
            && activeRuntime.Installation == entry.Installation
            && activeRuntime.Fence == runtimeFence
        );

    private PluginLifecycleCommandOutcome PublishConflict(
        PluginId pluginId,
        PluginLifecycleState? current
    )
    {
        if (current is null)
        {
            _ = _snapshots.Remove(pluginId);
        }
        else
        {
            _ = _snapshots.Publish(current, worker: null);
        }

        return Conflict(current);
    }
}
