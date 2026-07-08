namespace BlokeBot.Eventing;

public sealed record EventNotification<TKey>(TKey Key)
    where TKey : notnull;
