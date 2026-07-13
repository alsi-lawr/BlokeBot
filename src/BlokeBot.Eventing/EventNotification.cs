namespace BlokeBot.Eventing;

public sealed record EventNotification<TKey>(TKey Key, ObserverCorrelationId CorrelationId)
    where TKey : notnull;
