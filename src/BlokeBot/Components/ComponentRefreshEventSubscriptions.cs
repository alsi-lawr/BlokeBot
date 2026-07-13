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
        where TKey : notnull
    {
        return events.Subscribe(
            key,
            ObserverIdentity.For(RequireTargetType(stateHasChanged)),
            async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await invokeAsync(async () =>
                {
                    await reloadAsync();
                    stateHasChanged();
                });
            }
        );
    }

    public static EventSubscriptionSet SubscribeForComponentRefresh<TKey>(
        this EventBus<TKey> events,
        IEnumerable<TKey> keys,
        Func<Func<Task>, Task> invokeAsync,
        Func<Task> reloadAsync,
        Action stateHasChanged
    )
        where TKey : notnull
    {
        return events.Subscribe(
            keys,
            ObserverIdentity.For(RequireTargetType(stateHasChanged)),
            async (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await invokeAsync(async () =>
                {
                    await reloadAsync();
                    stateHasChanged();
                });
            }
        );
    }

    private static Type RequireTargetType(Action callback)
    {
        return callback.Target?.GetType()
        ?? throw new ArgumentException(
            "Component refresh callbacks must target a component instance.",
            nameof(callback)
        );
    }
}
