namespace BlokeBot.Eventing;

public abstract class EventNotifier<TKey>(EventBus<TKey> events, TKey key)
    where TKey : notnull
{
    public ValueTask<ObserverFanOutOutcome> NotifyChangedAsync(CancellationToken cancellationToken)
    {
        return events.PublishAsync(key, cancellationToken);
    }
}
