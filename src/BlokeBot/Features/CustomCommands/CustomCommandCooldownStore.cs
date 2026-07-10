using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandCooldownStore(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<CooldownKey, DateTimeOffset> lastUses = [];

    public bool TryRecord(
        int commandId,
        CustomCommandCooldownScope scope,
        string userLogin,
        TimeSpan cooldown
    )
    {
        if (cooldown <= TimeSpan.Zero)
            return true;

        var key = new CooldownKey(
            commandId,
            scope == CustomCommandCooldownScope.User
                ? TwitchLogin.Normalize(userLogin)
                : string.Empty
        );
        var now = clock.GetUtcNow();

        lock (gate)
        {
            if (lastUses.TryGetValue(key, out var lastUse) && now - lastUse < cooldown)
                return false;

            lastUses[key] = now;
            return true;
        }
    }

    private readonly record struct CooldownKey(int CommandId, string UserLogin);
}
