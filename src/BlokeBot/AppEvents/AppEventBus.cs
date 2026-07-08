using Microsoft.Extensions.Logging;

namespace BlokeBot.AppEvents;

public sealed class AppEventBus
{
    private readonly object gate = new();
    private readonly Dictionary<AppEventKind, List<Func<AppEvent, Task>>> subscriptions = [];
    private readonly ILogger<AppEventBus>? log;

    public AppEventBus() { }

    public AppEventBus(ILogger<AppEventBus> log)
    {
        this.log = log;
    }

    public IDisposable Subscribe(AppEventKind kind, Func<AppEvent, Task> handler)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(kind, out var handlers))
            {
                handlers = [];
                subscriptions[kind] = handlers;
            }

            handlers.Add(handler);
        }

        return new Subscription(this, kind, handler);
    }

    public async Task PublishAsync(AppEventKind kind)
    {
        Func<AppEvent, Task>[] handlers;
        lock (gate)
        {
            handlers = subscriptions.TryGetValue(kind, out var current) ? [.. current] : [];
        }

        var evt = new AppEvent(kind);
        foreach (var handler in handlers)
        {
            try
            {
                await handler(evt);
            }
            catch (Exception ex)
            {
                log?.LogError(ex, "App event subscriber failed for {EventKind}.", kind);
            }
        }
    }

    private void Unsubscribe(AppEventKind kind, Func<AppEvent, Task> handler)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(kind, out var handlers))
                return;

            handlers.Remove(handler);
            if (handlers.Count == 0)
                subscriptions.Remove(kind);
        }
    }

    private sealed class Subscription(
        AppEventBus owner,
        AppEventKind kind,
        Func<AppEvent, Task> handler
    ) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            owner.Unsubscribe(kind, handler);
        }
    }
}
