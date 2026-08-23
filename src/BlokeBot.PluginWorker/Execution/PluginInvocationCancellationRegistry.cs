using System.Collections.Concurrent;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.PluginWorker;

internal enum PluginInvocationCancellationAdmission
{
    Admitted,
    DuplicateInvocation,
    DeadlineExceeded,
}

internal sealed class PluginInvocationCancellationRegistry : IDisposable
{
    private readonly ConcurrentDictionary<PluginWorkerInvocationId, CancellationEntry> _entries =
        new();

    internal event Action<
        PluginWorkerInvocationIdentity,
        PluginCancellationReason
    >? CancellationRequested;

    internal PluginInvocationCancellationAdmission Begin(PluginWorkerInvocationIdentity identity)
    {
        var entry = new CancellationEntry(identity);
        if (!_entries.TryAdd(identity.InvocationId, entry))
        {
            entry.Dispose();
            return PluginInvocationCancellationAdmission.DuplicateInvocation;
        }

        var due = identity.Deadline.ToDateTimeOffset() - DateTimeOffset.UtcNow;
        if (due <= TimeSpan.Zero)
        {
            _ = _entries.TryRemove(identity.InvocationId, out _);
            entry.Dispose();
            return PluginInvocationCancellationAdmission.DeadlineExceeded;
        }

        entry.SetDeadline(due, () => Cancel(identity, PluginCancellationReason.DeadlineExceeded));
        return PluginInvocationCancellationAdmission.Admitted;
    }

    internal bool Cancel(PluginWorkerInvocationIdentity identity, PluginCancellationReason reason)
    {
        if (
            _entries.TryGetValue(identity.InvocationId, out var entry)
            && entry.Identity == identity
        )
        {
            if (entry.Cancel(reason))
            {
                CancellationRequested?.Invoke(entry.Identity, reason);
                return true;
            }
        }

        return false;
    }

    internal bool TryGetReason(
        PluginWorkerInvocationId invocationId,
        out PluginCancellationReason reason
    )
    {
        if (_entries.TryGetValue(invocationId, out var entry) && entry.TryGetReason(out reason))
        {
            return true;
        }

        reason = default;
        return false;
    }

    internal void Complete(PluginWorkerInvocationId invocationId)
    {
        if (_entries.TryRemove(invocationId, out var entry))
        {
            entry.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
    }

    private sealed class CancellationEntry(PluginWorkerInvocationIdentity identity) : IDisposable
    {
        private readonly object _sync = new();
        private PluginCancellationReason? _reason;
        private Timer? _deadline;

        internal PluginWorkerInvocationIdentity Identity { get; } = identity;

        internal void SetDeadline(TimeSpan due, Action callback)
        {
            lock (_sync)
            {
                _deadline = new Timer(_ => callback(), null, due, Timeout.InfiniteTimeSpan);
            }
        }

        internal bool Cancel(PluginCancellationReason reason)
        {
            lock (_sync)
            {
                if (_reason.HasValue)
                {
                    return false;
                }

                _reason = reason;
                return true;
            }
        }

        internal bool TryGetReason(out PluginCancellationReason reason)
        {
            lock (_sync)
            {
                if (_reason.HasValue)
                {
                    reason = _reason.Value;
                    return true;
                }
            }

            reason = default;
            return false;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _deadline?.Dispose();
                _deadline = null;
            }
        }
    }
}
