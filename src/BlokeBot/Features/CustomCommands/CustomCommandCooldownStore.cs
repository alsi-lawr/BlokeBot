using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandCooldownStore(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<CooldownKey, DateTimeOffset> blockedUntil = [];

    internal int EntryCount
    {
        get
        {
            lock (gate)
                return blockedUntil.Count;
        }
    }

    public bool TryRecord(
        int commandId,
        CustomCommandCooldownScope scope,
        string userLogin,
        TimeSpan cooldown
    )
    {
        var now = clock.GetUtcNow();

        lock (gate)
        {
            PruneExpired(now);
            if (cooldown <= TimeSpan.Zero)
                return true;

            var key = new CooldownKey(
                commandId,
                scope == CustomCommandCooldownScope.User
                    ? TwitchLogin.Normalize(userLogin)
                    : string.Empty
            );
            if (blockedUntil.TryGetValue(key, out var expiry) && expiry > now)
                return false;

            blockedUntil[key] = now + cooldown;
            return true;
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
            blockedUntil.Remove(key);
    }

    private readonly record struct CooldownKey(int CommandId, string UserLogin);
}
