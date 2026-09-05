using System.Threading.RateLimiting;

namespace BlokeBot.Core.Features.ViewerPortal;

// One request/circuit scope. A timed-out caller does not release an unfinished owner's slot.
internal sealed class PortalReadScheduler : IDisposable
{
    internal const int ParallelOwners = 4;
    private readonly ConcurrencyLimiter _slots = new(
        new ConcurrencyLimiterOptions
        {
            PermitLimit = ParallelOwners,
            QueueLimit = 64,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }
    );

    internal async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        using var lease = await _slots.AcquireAsync(1, ct);
        return lease.IsAcquired
            ? await read(ct)
            : throw new InvalidOperationException("The portal read queue is full.");
    }

    public void Dispose() => _slots.Dispose();
}
