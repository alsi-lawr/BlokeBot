namespace BlokeBot.Eventing;

public sealed class EventSubscriptionSet(IEnumerable<IDisposable> subscriptions) : IDisposable
{
    private readonly IDisposable[] subscriptions = [.. subscriptions];
    private bool disposed;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }
}
