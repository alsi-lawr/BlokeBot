namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatDuplicateCooldown
{
    private readonly object gate = new();
    private readonly Dictionary<PublicChatMessageKey, DateTimeOffset> blockedUntil = [];

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

            return blockedUntil.GetValueOrDefault(PublicChatMessageKey.From(message), now);
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

            blockedUntil[PublicChatMessageKey.From(message)] = now + cooldown;
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

internal readonly record struct PublicChatMessageKey(string Channel, string Message)
{
    public static PublicChatMessageKey From(TwitchOutboundChatMessage message) =>
        new(message.Channel.Trim().ToLowerInvariant(), message.Message.Trim());
}
