using BlokeBot.Eventing;

namespace BlokeBot.Components;

public static class ComponentRefreshEventSubscriptions
{
    public static IDisposable SubscribeForComponentRefresh<TKey>(
        this EventBus<TKey> events,
        TKey key,
        Func<Func<Task>, Task> invokeAsync,
        Func<Task> reloadAsync,
        Action stateHasChanged
    )
        where TKey : notnull =>
        events.Subscribe(
            key,
            _ =>
                invokeAsync(async () =>
                {
                    await reloadAsync();
                    stateHasChanged();
                })
        );

    public static EventSubscriptionSet SubscribeForComponentRefresh<TKey>(
        this EventBus<TKey> events,
        IEnumerable<TKey> keys,
        Func<Func<Task>, Task> invokeAsync,
        Func<Task> reloadAsync,
        Action stateHasChanged
    )
        where TKey : notnull =>
        events.Subscribe(
            keys,
            _ =>
                invokeAsync(async () =>
                {
                    await reloadAsync();
                    stateHasChanged();
                })
        );
}
