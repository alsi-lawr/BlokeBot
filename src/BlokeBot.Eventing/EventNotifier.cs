namespace BlokeBot.Eventing;

public abstract class EventNotifier<TKey>(EventBus<TKey> events, TKey key)
    where TKey : notnull
{
    public Task NotifyChangedAsync() => events.PublishAsync(key);
}
