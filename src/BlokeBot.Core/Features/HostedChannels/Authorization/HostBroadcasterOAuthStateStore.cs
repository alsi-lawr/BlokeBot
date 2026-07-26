using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

internal sealed class HostBroadcasterOAuthStateStore(TimeProvider timeProvider)
{
    private const string _prefix = "broadcaster.";
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    internal string Issue(string userId, int hostId)
    {
        var state = _prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _pending[state] = new(userId, hostId, timeProvider.GetUtcNow().AddMinutes(10));
        return state;
    }

    internal bool TryConsume(string? state, string userId, out int hostId)
    {
        hostId = 0;
        return state is not null
            && _pending.TryRemove(state, out var pending)
            && pending.ExpiresAtUtc > timeProvider.GetUtcNow()
            && pending.UserId == userId
            && (hostId = pending.HostId) > 0;
    }

    internal static bool IsState(string? state)
    {
        return state?.StartsWith(_prefix, StringComparison.Ordinal) == true;
    }

    private sealed record Pending(string UserId, int HostId, DateTimeOffset ExpiresAtUtc);
}
