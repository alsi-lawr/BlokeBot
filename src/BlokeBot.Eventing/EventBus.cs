using Microsoft.Extensions.Logging;

namespace BlokeBot.Eventing;

public sealed class EventBus<TKey>
    where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, List<Func<EventNotification<TKey>, Task>>> subscriptions = [];
    private readonly ILogger<EventBus<TKey>>? log;

    public EventBus() { }

    public EventBus(ILogger<EventBus<TKey>> log)
    {
        this.log = log;
    }

    public IDisposable Subscribe(TKey key, Func<EventNotification<TKey>, Task> handler)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var handlers))
            {
                handlers = [];
                subscriptions[key] = handlers;
            }

            handlers.Add(handler);
        }

        return new Subscription(this, key, handler);
    }

    public async Task PublishAsync(TKey key)
    {
        Func<EventNotification<TKey>, Task>[] handlers;
        lock (gate)
        {
            handlers = subscriptions.TryGetValue(key, out var current) ? [.. current] : [];
        }

        var notification = new EventNotification<TKey>(key);
        foreach (var handler in handlers)
        {
            try
            {
                await handler(notification);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "Event subscriber failed for {EventKey}.", key);
            }
        }
    }

    private void Unsubscribe(TKey key, Func<EventNotification<TKey>, Task> handler)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var handlers))
                return;

            handlers.Remove(handler);
            if (handlers.Count == 0)
                subscriptions.Remove(key);
        }
    }

    private sealed class Subscription(
        EventBus<TKey> owner,
        TKey key,
        Func<EventNotification<TKey>, Task> handler
    ) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            owner.Unsubscribe(key, handler);
        }
    }
}
