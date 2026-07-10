using BlokeBot.Twitch;

namespace BlokeBot.Features.Points.Gambling;

public sealed class PointsGamblingCooldownStore(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<CooldownKey, DateTimeOffset> lastUses = [];

    public bool TryRecord(int hostId, string userLogin, TimeSpan cooldown)
    {
        if (cooldown <= TimeSpan.Zero)
            return true;

        var key = new CooldownKey(hostId, TwitchLogin.Normalize(userLogin));
        var now = clock.GetUtcNow();

        lock (gate)
        {
            if (lastUses.TryGetValue(key, out var lastUse) && now - lastUse < cooldown)
                return false;

            lastUses[key] = now;
            return true;
        }
    }

    private readonly record struct CooldownKey(int HostId, string UserLogin);
}
