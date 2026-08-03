namespace BlokeBot.Eventing;

public sealed class EventBus<TKey>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, List<RegisteredObserver>> _subscriptions = [];
    private readonly ObserverFanOut<
        EventBusObserverBoundary<TKey>,
        EventNotification<TKey>,
        EventBusDeadLetter
    > _fanOut;
    private readonly EventBusEventIdentity<TKey> _eventIdentity;

    internal EventBus(
        ObserverFanOut<
            EventBusObserverBoundary<TKey>,
            EventNotification<TKey>,
            EventBusDeadLetter
        > fanOut,
        EventBusEventIdentity<TKey> eventIdentity
    )
    {
        _fanOut = fanOut;
        _eventIdentity = eventIdentity;
    }

    public IDisposable Subscribe(
        TKey key,
        ObserverIdentity observer,
        Func<EventNotification<TKey>, CancellationToken, ValueTask> handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new RegisteredObserver(observer, handler);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(key, out var handlers))
            {
                handlers = [];
                _subscriptions[key] = handlers;
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

        return new EventSubscriptionSet(keys.Select(key => Subscribe(key, observer, handler)));
    }

    public ValueTask<ObserverFanOutOutcome> PublishAsync(
        TKey key,
        CancellationToken cancellationToken
    )
    {
        RegisteredObserver[] handlers;
        lock (_gate)
        {
            handlers = _subscriptions.TryGetValue(key, out var current) ? [.. current] : [];
        }

        var identity = _eventIdentity.Project(key);
        return _fanOut.DispatchAsync(
            handlers,
            correlationId =>
            {
                var notification = new EventNotification<TKey>(key, correlationId);
                return new ObserverDispatch<EventNotification<TKey>, EventBusDeadLetter>
                {
                    Event = notification,
                    EventIdentity = identity,
                    DeadLetter = new EventBusDeadLetter(identity),
                };
            },
            registration => registration.Identity,
            (registration, notification, token) => registration.Handler(notification, token),
            cancellationToken
        );
    }

    private void Unsubscribe(TKey key, RegisteredObserver observer)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(key, out var handlers))
            {
                return;
            }

            _ = handlers.Remove(observer);
            if (handlers.Count == 0)
            {
                _ = _subscriptions.Remove(key);
            }
        }
    }

    private sealed record RegisteredObserver(
        ObserverIdentity Identity,
        Func<EventNotification<TKey>, CancellationToken, ValueTask> Handler
    );

    private sealed class Subscription(EventBus<TKey> owner, TKey key, RegisteredObserver observer)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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

internal sealed record EventBusDeadLetter(ObserverEventIdentity Event) : IObserverDeadLetterPayload;
