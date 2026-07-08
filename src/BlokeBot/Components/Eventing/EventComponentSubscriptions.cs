using BlokeBot.Eventing;

namespace BlokeBot.Components;

public static class EventComponentSubscriptions
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

    public static IDisposable SubscribeForComponentRefresh<TKey>(
        this EventBus<TKey> events,
        IReadOnlyCollection<TKey> keys,
        Func<Func<Task>, Task> invokeAsync,
        Func<Task> reloadAsync,
        Action stateHasChanged
    )
        where TKey : notnull =>
        new EventSubscriptionSet(
            keys.Select(key =>
                events.SubscribeForComponentRefresh(key, invokeAsync, reloadAsync, stateHasChanged)
            )
        );
}
