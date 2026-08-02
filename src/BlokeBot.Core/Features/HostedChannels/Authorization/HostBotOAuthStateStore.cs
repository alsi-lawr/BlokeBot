using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

internal sealed class HostBotOAuthStateStore(TimeProvider timeProvider)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string _prefix = "host-bot.";

    private readonly ConcurrentDictionary<string, PendingHostBotOAuthState> _states = new(
        StringComparer.Ordinal
    );

    internal string Issue(string authenticatedUserId, int hostId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedUserId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostId);

        RemoveExpired();
        while (true)
        {
            var state = _prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var pending = new PendingHostBotOAuthState(
                authenticatedUserId,
                hostId,
                HostBotOAuthPurpose.CustomBot,
                timeProvider.GetUtcNow().Add(Lifetime)
            );
            if (_states.TryAdd(state, pending))
            {
                return state;
            }
        }
    }

    internal HostBotOAuthStateConsumption Consume(string? state, string authenticatedUserId)
    {
        if (
            string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(authenticatedUserId)
            || !_states.TryRemove(state, out var pending)
            || pending.ExpiresAtUtc <= timeProvider.GetUtcNow()
            || !string.Equals(
                pending.AuthenticatedUserId,
                authenticatedUserId,
                StringComparison.Ordinal
            )
            || pending.Purpose is not HostBotOAuthPurpose.CustomBot
        )
        {
            return new HostBotOAuthStateConsumption.Rejected();
        }

        return new HostBotOAuthStateConsumption.Consumed(pending.HostId);
    }

    internal static bool IsHostBotState(string? state) =>
        state?.StartsWith(_prefix, StringComparison.Ordinal) == true;

    private void RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var state in _states)
        {
            if (state.Value.ExpiresAtUtc <= now)
            {
                _states.TryRemove(state.Key, out _);
            }
        }
    }
}

internal enum HostBotOAuthPurpose
{
    CustomBot,
}

internal sealed record PendingHostBotOAuthState(
    string AuthenticatedUserId,
    int HostId,
    HostBotOAuthPurpose Purpose,
    DateTimeOffset ExpiresAtUtc
);

internal abstract record HostBotOAuthStateConsumption
{
    private HostBotOAuthStateConsumption() { }

    internal sealed record Consumed(int HostId) : HostBotOAuthStateConsumption;

    internal sealed record Rejected : HostBotOAuthStateConsumption;
}
