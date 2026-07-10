namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchOutboundDuplicateCooldown
{
    private readonly object gate = new();
    private readonly Dictionary<TwitchOutboundMessageKey, DateTimeOffset> blockedUntil = [];

    internal int EntryCount
    {
        get
        {
            lock (gate)
                return blockedUntil.Count;
        }
    }

    public DateTimeOffset NextAllowedAt(
        TwitchOutboundChatMessage message,
        DateTimeOffset now,
        TimeSpan cooldown
    )
    {
        lock (gate)
        {
            PruneExpired(now);
            if (cooldown <= TimeSpan.Zero)
                return now;

            return blockedUntil.GetValueOrDefault(TwitchOutboundMessageKey.From(message), now);
        }
    }

    public void RecordSent(
        TwitchOutboundChatMessage message,
        DateTimeOffset now,
        TimeSpan cooldown
    )
    {
        lock (gate)
        {
            PruneExpired(now);
            if (cooldown <= TimeSpan.Zero)
                return;

            blockedUntil[TwitchOutboundMessageKey.From(message)] = now + cooldown;
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (
            var key in blockedUntil
                .Where(pair => pair.Value <= now)
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            blockedUntil.Remove(key);
        }
    }
}

internal readonly record struct TwitchOutboundMessageKey(string Channel, string Message)
{
    public static TwitchOutboundMessageKey From(TwitchOutboundChatMessage message) =>
        new(message.Channel.Trim().ToLowerInvariant(), message.Message.Trim());
}
