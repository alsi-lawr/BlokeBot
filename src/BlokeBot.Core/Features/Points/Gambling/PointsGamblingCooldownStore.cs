namespace BlokeBot.Core.Features.Points.Gambling;

public sealed class PointsGamblingCooldownStore(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<CooldownKey, DateTimeOffset> _blockedUntil = [];

    internal int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _blockedUntil.Count;
            }
        }
    }

    public bool TryRecord(int hostId, string userLogin, TimeSpan cooldown)
    {
        var now = clock.GetUtcNow();

        lock (_gate)
        {
            PruneExpired(now);
            if (cooldown <= TimeSpan.Zero)
            {
                return true;
            }

            var key = new CooldownKey(hostId, Login.Normalize(userLogin));
            if (_blockedUntil.TryGetValue(key, out var expiry) && expiry > now)
            {
                return false;
            }

            _blockedUntil[key] = now + cooldown;
            return true;
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (
            var key in _blockedUntil
                .Where(pair => pair.Value <= now)
                .Select(pair => pair.Key)
                .ToArray()
        )
        {
            _blockedUntil.Remove(key);
        }
    }

    private readonly record struct CooldownKey(int HostId, string UserLogin);
}
