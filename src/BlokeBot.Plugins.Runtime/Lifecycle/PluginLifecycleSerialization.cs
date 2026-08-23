using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public interface IPluginLifecycleSerialization
{
    ValueTask<PluginLifecycleMutationLease> AcquireAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    );
}

public sealed class PluginLifecycleMutationLease : IAsyncDisposable
{
    private readonly PluginLifecycleSerialization _owner;
    private int _disposed;

    internal PluginLifecycleMutationLease(
        PluginLifecycleSerialization owner,
        PluginId pluginId,
        SemaphoreSlim gate
    )
    {
        _owner = owner;
        PluginId = pluginId;
        Gate = gate;
    }

    public PluginId PluginId { get; }

    internal SemaphoreSlim Gate { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.Release(this);
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class PluginLifecycleSerialization : IPluginLifecycleSerialization
{
    private readonly object _sync = new();
    private readonly Dictionary<PluginId, GateEntry> _gates = [];

    public async ValueTask<PluginLifecycleMutationLease> AcquireAsync(
        PluginId pluginId,
        CancellationToken cancellationToken
    )
    {
        GateEntry entry;
        lock (_sync)
        {
            if (!_gates.TryGetValue(pluginId, out entry!))
            {
                entry = new();
                _gates.Add(pluginId, entry);
            }

            entry.References++;
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken);
            return new(this, pluginId, entry.Gate);
        }
        catch
        {
            ReleaseReference(pluginId, entry);
            throw;
        }
    }

    internal void Release(PluginLifecycleMutationLease lease)
    {
        _ = lease.Gate.Release();
        lock (_sync)
        {
            var entry = _gates[lease.PluginId];
            entry.References--;
            if (entry.References == 0)
            {
                _ = _gates.Remove(lease.PluginId);
                entry.Gate.Dispose();
            }
        }
    }

    private void ReleaseReference(PluginId pluginId, GateEntry entry)
    {
        lock (_sync)
        {
            entry.References--;
            if (entry.References == 0)
            {
                _ = _gates.Remove(pluginId);
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class GateEntry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        internal int References { get; set; }
    }
}
