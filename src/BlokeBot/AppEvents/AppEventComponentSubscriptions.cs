namespace BlokeBot.AppEvents;

public static class AppEventComponentSubscriptions
{
    public static IDisposable SubscribeForComponentRefresh(
        this AppEventBus events,
        AppEventKind kind,
        Func<Func<Task>, Task> invokeAsync,
        Func<Task> reloadAsync,
        Action stateHasChanged
    ) =>
        events.Subscribe(
            kind,
            _ =>
                invokeAsync(async () =>
                {
                    await reloadAsync();
                    stateHasChanged();
                })
        );

    public static IDisposable SubscribeForComponentRefresh(
        this AppEventBus events,
        IReadOnlyCollection<AppEventKind> kinds,
        Func<Func<Task>, Task> invokeAsync,
        Func<Task> reloadAsync,
        Action stateHasChanged
    ) =>
        new AppEventSubscriptionSet(
            kinds.Select(kind =>
                events.SubscribeForComponentRefresh(kind, invokeAsync, reloadAsync, stateHasChanged)
            )
        );
}

public sealed class AppEventSubscriptionSet(IEnumerable<IDisposable> subscriptions) : IDisposable
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
