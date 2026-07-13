namespace BlokeBot.Eventing;

public sealed class EventSubscriptionSet(IEnumerable<IDisposable> subscriptions) : IDisposable
{
    private readonly IDisposable[] _subscriptions = [.. subscriptions];
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }
}
