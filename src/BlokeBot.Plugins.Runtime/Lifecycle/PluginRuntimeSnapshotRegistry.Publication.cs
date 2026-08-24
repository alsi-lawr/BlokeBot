using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed partial class PluginRuntimeSnapshotRegistry
{
    internal PluginRuntimeSlot? Publish(
        PluginLifecycleState state,
        IPluginLifecycleWorkerSession? worker
    )
    {
        var slot = new PluginRuntimeSlot(
            Entry(state, worker?.Mode),
            new(),
            worker,
            worker is null ? null : state.SelectedFence
        );
        return Replace(state.PluginId, slot).Previous;
    }

    internal PluginAdmissionStopPublication StopAdmission(PluginLifecycleState state)
    {
        var runtimeFence = state.ActiveRuntime?.Fence;
        PluginRuntimeSlot? previous;
        PluginRuntimeSlot stopped;
        ChangeNotification notification;
        lock (_sync)
        {
            _ = _current.Slots.TryGetValue(state.PluginId, out previous);
            var retained = previous?.RuntimeFence == runtimeFence ? previous : null;
            stopped = new(
                Entry(state, retained?.Worker?.Mode),
                retained?.Tracker ?? new(),
                retained?.Worker,
                runtimeFence
            );
            notification = PublishLocked(_current.Slots.SetItem(state.PluginId, stopped));
        }

        Notify(notification);
        return new(state.PluginId, previous, stopped, runtimeFence is null ? null : stopped);
    }

    internal PluginRuntimeSlot? Remove(PluginId pluginId)
    {
        PluginRuntimeSlot? previous;
        ChangeNotification notification;
        lock (_sync)
        {
            _ = _current.Slots.TryGetValue(pluginId, out previous);
            notification = PublishLocked(_current.Slots.Remove(pluginId));
        }

        Notify(notification);
        return previous;
    }

    internal PluginRuntimeRollbackOutcome TryRestoreOriginal(
        PluginAdmissionStopPublication publication
    )
    {
        ChangeNotification notification;
        lock (_sync)
        {
            if (
                !_current.Slots.TryGetValue(publication.PluginId, out var current)
                || !ReferenceEquals(current, publication.Stopped)
            )
            {
                return new PluginRuntimeRollbackOutcome.PublicationChanged();
            }

            if (publication.Original?.Worker is { Termination.IsCompleted: true } worker)
            {
                return new PluginRuntimeRollbackOutcome.TerminationObserved(
                    worker.Termination.GetAwaiter().GetResult()
                );
            }

            notification = PublishLocked(
                publication.Original is null
                    ? _current.Slots.Remove(publication.PluginId)
                    : _current.Slots.SetItem(publication.PluginId, publication.Original)
            );
        }

        Notify(notification);
        return new PluginRuntimeRollbackOutcome.Restored();
    }

    private (PluginRuntimeSlot? Previous, PluginRuntimeSlot Current) Replace(
        PluginId pluginId,
        PluginRuntimeSlot current
    )
    {
        PluginRuntimeSlot? previous;
        ChangeNotification notification;
        lock (_sync)
        {
            _ = _current.Slots.TryGetValue(pluginId, out previous);
            notification = PublishLocked(_current.Slots.SetItem(pluginId, current));
        }

        Notify(notification);
        return (previous, current);
    }

    private ChangeNotification PublishLocked(ImmutableDictionary<PluginId, PluginRuntimeSlot> slots)
    {
        Volatile.Write(ref _current, new PluginRuntimeSnapshot(slots));
        var notification = new ChangeNotification(_change, new(++_changeVersion));
        _change = NewChangeCompletion();
        return notification;
    }

    private static void Notify(ChangeNotification notification) =>
        notification.Completion.TrySetResult(notification.Version);

    private static PluginRuntimeEntry Entry(
        PluginLifecycleState state,
        PluginWorkerMode? workerMode
    ) =>
        new(
            state.SelectedInstallation,
            state.Phase,
            state.OperationId,
            state.SelectedGeneration,
            workerMode
        );

    private sealed record ChangeNotification(
        TaskCompletionSource<PluginLifecycleChangeVersion> Completion,
        PluginLifecycleChangeVersion Version
    );
}
