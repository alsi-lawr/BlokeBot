namespace BlokeBot.Eventing;

public sealed class EventBus<TKey>
    where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, List<RegisteredObserver>> subscriptions = [];
    private readonly ObserverFanOut<
        EventBusObserverBoundary<TKey>,
        EventNotification<TKey>,
        EventBusDeadLetter
    > fanOut;
    private readonly EventBusEventIdentity<TKey> eventIdentity;

    internal EventBus(
        ObserverFanOut<
            EventBusObserverBoundary<TKey>,
            EventNotification<TKey>,
            EventBusDeadLetter
        > fanOut,
        EventBusEventIdentity<TKey> eventIdentity
    )
    {
        this.fanOut = fanOut;
        this.eventIdentity = eventIdentity;
    }

    public IDisposable Subscribe(
        TKey key,
        ObserverIdentity observer,
        Func<EventNotification<TKey>, CancellationToken, ValueTask> handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new RegisteredObserver(observer, handler);
        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var handlers))
            {
                handlers = [];
                subscriptions[key] = handlers;
            }

            handlers.Add(registration);
        }

        return new Subscription(this, key, registration);
    }

    public EventSubscriptionSet Subscribe(
        IEnumerable<TKey> keys,
        ObserverIdentity observer,
        Func<EventNotification<TKey>, CancellationToken, ValueTask> handler
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(handler);

        return new EventSubscriptionSet(
            keys.Select(key => Subscribe(key, observer, handler))
        );
    }

    public ValueTask<ObserverFanOutOutcome> PublishAsync(
        TKey key,
        CancellationToken cancellationToken
    )
    {
        RegisteredObserver[] handlers;
        lock (gate)
        {
            handlers = subscriptions.TryGetValue(key, out var current)
                ? [.. current]
                : [];
        }

        var identity = eventIdentity.Project(key);
        return fanOut.DispatchAsync(
            handlers,
            correlationId =>
            {
                var notification = new EventNotification<TKey>(key, correlationId);
                return new ObserverDispatch<
                    EventNotification<TKey>,
                    EventBusDeadLetter
                >
                {
                    Event = notification,
                    EventIdentity = identity,
                    DeadLetter = new EventBusDeadLetter(identity),
                };
            },
            registration => registration.Identity,
            (registration, notification, token) =>
                registration.Handler(notification, token),
            cancellationToken
        );
    }

    private void Unsubscribe(TKey key, RegisteredObserver observer)
    {
        lock (gate)
        {
            if (!subscriptions.TryGetValue(key, out var handlers))
                return;

            handlers.Remove(observer);
            if (handlers.Count == 0)
                subscriptions.Remove(key);
        }
    }

    private sealed record RegisteredObserver(
        ObserverIdentity Identity,
        Func<EventNotification<TKey>, CancellationToken, ValueTask> Handler
    );

    private sealed class Subscription(
        EventBus<TKey> owner,
        TKey key,
        RegisteredObserver observer
    ) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            owner.Unsubscribe(key, observer);
        }
    }
}

internal sealed class EventBusObserverBoundary<TKey>
    where TKey : notnull;

internal sealed record EventBusEventIdentity<TKey>
    where TKey : notnull
{
    internal required Func<TKey, ObserverEventIdentity> Project { get; init; }
}

internal sealed record EventBusDeadLetter(ObserverEventIdentity Event)
    : IObserverDeadLetterPayload;
