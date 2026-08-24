using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginRuntimeEntry(
    PluginInstallationIdentity Installation,
    PluginLifecyclePhase Phase,
    PluginLifecycleOperationId OperationId,
    PluginWorkerGeneration Generation,
    PluginWorkerMode? WorkerMode
)
{
    public PluginLifecycleFence Fence => new(OperationId, Generation);
}

public sealed class PluginRuntimeSnapshot
{
    internal PluginRuntimeSnapshot(ImmutableDictionary<PluginId, PluginRuntimeSlot> slots) =>
        Slots = slots;

    internal ImmutableDictionary<PluginId, PluginRuntimeSlot> Slots { get; }

    public IReadOnlyDictionary<PluginId, PluginRuntimeEntry> Entries =>
        Slots.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.Entry);

    public static PluginRuntimeSnapshot Empty { get; } =
        new(ImmutableDictionary<PluginId, PluginRuntimeSlot>.Empty);
}

public enum PluginFeatureAdmissionReadiness
{
    Ready,
    Disabled,
    NotReady,
}

public enum PluginAdmissionRejectionCode
{
    Missing,
    Disabled,
    Removed,
    Faulted,
    StaleOperation,
    StaleGeneration,
    NotReady,
    NotActive,
    Staging,
}

public abstract record PluginAdmissionOutcome
{
    private PluginAdmissionOutcome() { }

    public sealed record Admitted(PluginRuntimeAdmission Admission) : PluginAdmissionOutcome;

    public sealed record Rejected(PluginAdmissionRejectionCode Code) : PluginAdmissionOutcome;
}

public enum PluginFenceRejectionCode
{
    Missing,
    StaleOperation,
    StaleGeneration,
    NotActive,
}

public abstract record PluginFenceOutcome
{
    private PluginFenceOutcome() { }

    public sealed record Current : PluginFenceOutcome;

    public sealed record Rejected(PluginFenceRejectionCode Code) : PluginFenceOutcome;
}

public sealed class PluginRuntimeAdmission : IAsyncDisposable
{
    private readonly PluginAdmissionTracker _tracker;
    private int _disposed;

    internal PluginRuntimeAdmission(PluginRuntimeEntry entry, PluginAdmissionTracker tracker)
    {
        Entry = entry;
        _tracker = tracker;
    }

    public PluginRuntimeEntry Entry { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _tracker.Complete();
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed record PluginRuntimeSlot(
    PluginRuntimeEntry Entry,
    PluginAdmissionTracker Tracker,
    IPluginLifecycleWorkerSession? Worker,
    PluginLifecycleFence? RuntimeFence
);

internal sealed record PluginAdmissionStopPublication(
    PluginId PluginId,
    PluginRuntimeSlot? Original,
    PluginRuntimeSlot Stopped,
    PluginRuntimeSlot? Ownership
);

internal enum PluginRuntimeRollbackOutcome
{
    Restored,
    WorkerTerminated,
    PublicationChanged,
}

internal sealed class PluginAdmissionTracker
{
    private readonly object _sync = new();
    private TaskCompletionSource _drained = Completed();
    private int _active;

    internal void Admit()
    {
        lock (_sync)
        {
            if (_active++ == 0)
            {
                _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    internal void Complete()
    {
        lock (_sync)
        {
            if (_active == 0)
            {
                return;
            }

            if (--_active == 0)
            {
                _ = _drained.TrySetResult();
            }
        }
    }

    internal Task Drained()
    {
        lock (_sync)
        {
            return _drained.Task;
        }
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        completion.SetResult();
        return completion;
    }
}
