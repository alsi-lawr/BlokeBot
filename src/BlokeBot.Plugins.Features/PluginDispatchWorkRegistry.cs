namespace BlokeBot.Plugins.Features;

public abstract record PluginDispatchWorkAdmission
{
    private PluginDispatchWorkAdmission() { }

    public sealed record Admitted(PluginDispatchWorkLease Lease) : PluginDispatchWorkAdmission;

    public sealed record Stopping : PluginDispatchWorkAdmission;
}

public interface IPluginFeatureWorkCoordinator
{
    ValueTask CancelAndDrainAsync(PluginFeatureState state, CancellationToken cancellationToken);

    ValueTask CancelAndDrainPluginAsync(
        BlokeBot.Plugins.Contracts.PluginId pluginId,
        CancellationToken cancellationToken
    );

    void Resume(PluginFeatureState state);
}

public sealed class PluginDispatchWorkLease : IAsyncDisposable
{
    private readonly PluginDispatchWorkRegistry _owner;
    private readonly CancellationTokenSource _cancellation;
    private int _disposed;

    internal PluginDispatchWorkLease(
        PluginDispatchWorkRegistry owner,
        Guid id,
        PluginFeatureState state,
        CancellationTokenSource cancellation
    )
    {
        _owner = owner;
        Id = id;
        State = state;
        _cancellation = cancellation;
    }

    internal Guid Id { get; }

    internal PluginFeatureState State { get; }

    public CancellationToken CancellationToken => _cancellation.Token;

    internal void Cancel() => _cancellation.Cancel();

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.Complete(this);
            _cancellation.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class PluginDispatchWorkRegistry : IPluginFeatureWorkCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, PluginDispatchWorkLease> _active = [];
    private readonly HashSet<(PluginFeatureKey Key, PluginFeatureFence Fence)> _stopping = [];
    private TaskCompletionSource _changed = NewChange();

    public PluginDispatchWorkAdmission Admit(
        PluginFeatureState state,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            if (_stopping.Contains((state.Key, new(state.Fence, state.Generation))))
            {
                return new PluginDispatchWorkAdmission.Stopping();
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var lease = new PluginDispatchWorkLease(this, Guid.NewGuid(), state, linked);
            _active.Add(lease.Id, lease);
            return new PluginDispatchWorkAdmission.Admitted(lease);
        }
    }

    public ValueTask CancelAndDrainAsync(
        PluginFeatureState state,
        CancellationToken cancellationToken
    ) =>
        CancelAndDrainAsync(
            candidate => candidate.State.Key == state.Key && candidate.State.Fence == state.Fence,
            () => _stopping.Add((state.Key, new(state.Fence, state.Generation))),
            cancellationToken
        );

    public ValueTask CancelAndDrainPluginAsync(
        BlokeBot.Plugins.Contracts.PluginId pluginId,
        CancellationToken cancellationToken
    ) =>
        CancelAndDrainAsync(
            candidate => candidate.State.Key.PluginId == pluginId,
            () =>
            {
                foreach (
                    var state in _active
                        .Values.Where(candidate => candidate.State.Key.PluginId == pluginId)
                        .Select(static candidate => candidate.State)
                )
                {
                    _ = _stopping.Add((state.Key, new(state.Fence, state.Generation)));
                }
            },
            cancellationToken
        );

    public void Resume(PluginFeatureState state)
    {
        lock (_sync)
        {
            _ = _stopping.Remove((state.Key, new(state.Fence, state.Generation)));
        }
    }

    internal void Complete(PluginDispatchWorkLease lease)
    {
        TaskCompletionSource changed;
        lock (_sync)
        {
            _ = _active.Remove(lease.Id);
            changed = _changed;
            _changed = NewChange();
        }
        _ = changed.TrySetResult();
    }

    private async ValueTask CancelAndDrainAsync(
        Func<PluginDispatchWorkLease, bool> predicate,
        Action markStopping,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            PluginDispatchWorkLease[] active;
            Task changed;
            lock (_sync)
            {
                markStopping();
                active = _active.Values.Where(predicate).ToArray();
                if (active.Length == 0)
                {
                    return;
                }
                changed = _changed.Task;
                foreach (var lease in active)
                {
                    lease.Cancel();
                }
            }
            await changed.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource NewChange() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
