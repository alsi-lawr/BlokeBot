using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public enum PluginWorkerReservationFailureCode
{
    AdmittedWorkerExists,
    StagingWorkerExists,
}

public sealed record PluginWorkerReservationFailure(PluginWorkerReservationFailureCode Code);

public abstract record PluginWorkerReservationOutcome
{
    private PluginWorkerReservationOutcome() { }

    public sealed record Started(PluginWorkerLease Lease) : PluginWorkerReservationOutcome;

    public sealed record Rejected(PluginWorkerReservationFailure Failure)
        : PluginWorkerReservationOutcome;

    public sealed record StartFailed(PluginWorkerStartOutcome Failure)
        : PluginWorkerReservationOutcome;
}

public sealed class PluginWorkerLease : IAsyncDisposable
{
    private readonly PluginWorkerCoordinator _owner;
    private int _disposed;

    internal PluginWorkerLease(
        PluginWorkerCoordinator owner,
        PluginId pluginId,
        PluginWorkerMode mode,
        PluginWorkerClient client
    )
    {
        _owner = owner;
        PluginId = pluginId;
        Mode = mode;
        Client = client;
    }

    public PluginId PluginId { get; }

    public PluginWorkerMode Mode { get; }

    public PluginWorkerClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await Client.DisposeAsync();
        }
        finally
        {
            _owner.Release(this);
        }
    }
}

public sealed class PluginWorkerCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<PluginId, WorkerSlots> _workers = [];

    public async ValueTask<PluginWorkerReservationOutcome> StartAsync(
        PluginWorkerStartOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var pluginId = options.Package.Descriptor.Plugin.PluginId;
        if (!TryReserve(pluginId, options.Mode, out var failure))
        {
            return new PluginWorkerReservationOutcome.Rejected(failure!);
        }

        var reservationHeld = true;
        try
        {
            var started = await PluginWorkerClient.StartAsync(options, cancellationToken);
            if (started is not PluginWorkerStartOutcome.Started client)
            {
                return new PluginWorkerReservationOutcome.StartFailed(started);
            }

            var lease = new PluginWorkerLease(this, pluginId, options.Mode, client.Client);
            lock (_sync)
            {
                var slots = _workers[pluginId];
                slots.Set(options.Mode, lease);
                reservationHeld = false;
            }

            return new PluginWorkerReservationOutcome.Started(lease);
        }
        finally
        {
            if (reservationHeld)
            {
                ReleaseReservation(pluginId, options.Mode);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        PluginWorkerLease[] leases;
        lock (_sync)
        {
            leases = _workers.Values.SelectMany(slots => slots.Leases()).ToArray();
        }

        foreach (var lease in leases)
        {
            await lease.DisposeAsync();
        }
    }

    internal void Release(PluginWorkerLease lease)
    {
        lock (_sync)
        {
            if (!_workers.TryGetValue(lease.PluginId, out var slots))
            {
                return;
            }

            slots.Clear(lease.Mode, lease);
            RemoveEmpty(lease.PluginId, slots);
        }
    }

    private bool TryReserve(
        PluginId pluginId,
        PluginWorkerMode mode,
        out PluginWorkerReservationFailure? failure
    )
    {
        lock (_sync)
        {
            if (!_workers.TryGetValue(pluginId, out var slots))
            {
                slots = new();
                _workers.Add(pluginId, slots);
            }

            if (slots.IsReserved(mode))
            {
                failure = new(
                    mode == PluginWorkerMode.Admitted
                        ? PluginWorkerReservationFailureCode.AdmittedWorkerExists
                        : PluginWorkerReservationFailureCode.StagingWorkerExists
                );
                return false;
            }

            slots.Reserve(mode);
            failure = null;
            return true;
        }
    }

    private void ReleaseReservation(PluginId pluginId, PluginWorkerMode mode)
    {
        lock (_sync)
        {
            var slots = _workers[pluginId];
            slots.Unreserve(mode);
            RemoveEmpty(pluginId, slots);
        }
    }

    private void RemoveEmpty(PluginId pluginId, WorkerSlots slots)
    {
        if (slots.IsEmpty)
        {
            _ = _workers.Remove(pluginId);
        }
    }

    private sealed class WorkerSlots
    {
        private PluginWorkerLease? _admitted;
        private PluginWorkerLease? _staging;
        private bool _admittedReserved;
        private bool _stagingReserved;

        internal bool IsEmpty =>
            _admitted is null && _staging is null && !_admittedReserved && !_stagingReserved;

        internal bool IsReserved(PluginWorkerMode mode) =>
            mode == PluginWorkerMode.Admitted
                ? _admittedReserved || _admitted is not null
                : _stagingReserved || _staging is not null;

        internal void Reserve(PluginWorkerMode mode)
        {
            if (mode == PluginWorkerMode.Admitted)
            {
                _admittedReserved = true;
            }
            else
            {
                _stagingReserved = true;
            }
        }

        internal void Unreserve(PluginWorkerMode mode)
        {
            if (mode == PluginWorkerMode.Admitted)
            {
                _admittedReserved = false;
            }
            else
            {
                _stagingReserved = false;
            }
        }

        internal void Set(PluginWorkerMode mode, PluginWorkerLease lease)
        {
            Unreserve(mode);
            if (mode == PluginWorkerMode.Admitted)
            {
                _admitted = lease;
            }
            else
            {
                _staging = lease;
            }
        }

        internal void Clear(PluginWorkerMode mode, PluginWorkerLease lease)
        {
            if (mode == PluginWorkerMode.Admitted && ReferenceEquals(_admitted, lease))
            {
                _admitted = null;
            }
            else if (mode == PluginWorkerMode.Staging && ReferenceEquals(_staging, lease))
            {
                _staging = null;
            }
        }

        internal IEnumerable<PluginWorkerLease> Leases()
        {
            if (_admitted is not null)
            {
                yield return _admitted;
            }

            if (_staging is not null)
            {
                yield return _staging;
            }
        }
    }
}
