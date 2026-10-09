using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using BlokeBot.Core.Features.HostedChannels.Authorization;

namespace BlokeBot.Core.Features.CustomCommands;

internal interface IMessageLibraryRandomSource
{
    int Next(int exclusiveMaximum);

    int NextInclusive(int minimum, int maximum);
}

internal sealed class CryptographicMessageLibraryRandomSource : IMessageLibraryRandomSource
{
    public int Next(int exclusiveMaximum) => RandomNumberGenerator.GetInt32(exclusiveMaximum);

    public int NextInclusive(int minimum, int maximum)
    {
        var range = (ulong)((long)maximum - minimum) + 1;
        var sampleSpace = 1UL << 32;
        var acceptedMaximum = sampleSpace - (sampleSpace % range);
        uint sample;
        do
        {
            sample = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint)));
        } while (sample >= acceptedMaximum);

        return checked((int)(minimum + (long)(sample % range)));
    }
}

internal sealed record MessageLibraryRenderHost(int Id, string Login, string TwitchUserId);

internal interface IMessageLibraryChatterSource
{
    Task<ImmutableArray<HelixChatter>> GetAsync(
        MessageLibraryRenderHost host,
        CancellationToken cancellationToken
    );
}

internal sealed class MessageLibraryChatterSource(
    IHostBotAccountTokenStatusProvider botAccounts,
    HelixClient helix,
    BotSettings settings,
    TimeProvider clock
) : IMessageLibraryChatterSource
{
    private static readonly TimeSpan _snapshotLifetime = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<CacheKey, SemaphoreSlim> _refreshGates = new();

    public async Task<ImmutableArray<HelixChatter>> GetAsync(
        MessageLibraryRenderHost host,
        CancellationToken cancellationToken
    )
    {
        var active = await botAccounts.GetActiveTokenStatusAsync(
            host.Login,
            [Scopes.ModeratorReadChatters],
            cancellationToken
        );
        if (active.Status is not TokenStatus.Ready ready)
        {
            return [];
        }

        var key = new CacheKey(host.Id, ready.Validation.UserId);
        RemoveExpiredAndSuperseded(key);
        if (Fresh(key) is { } fresh)
        {
            return fresh;
        }

        var gate = _refreshGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh(key) is { } refreshed)
            {
                return refreshed;
            }

            var outcome = await helix.GetChattersAsync(
                new(settings.Identity.ClientId, ready.AccessToken),
                host.TwitchUserId,
                ready.Validation.UserId,
                cancellationToken
            );
            if (outcome is not HelixChattersOutcome.Complete complete)
            {
                return [];
            }

            var chatters = complete
                .Chatters.Where(chatter =>
                    !string.Equals(
                        chatter.UserId,
                        ready.Validation.UserId,
                        StringComparison.Ordinal
                    )
                )
                .ToImmutableArray();
            _cache[key] = new(chatters, clock.GetUtcNow().Add(_snapshotLifetime));
            return chatters;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private ImmutableArray<HelixChatter>? Fresh(CacheKey key) =>
        _cache.TryGetValue(key, out var entry) && entry.ExpiresAt > clock.GetUtcNow()
            ? entry.Chatters
            : null;

    private void RemoveExpiredAndSuperseded(CacheKey active)
    {
        var now = clock.GetUtcNow();
        foreach (var cached in _cache)
        {
            if (
                cached.Value.ExpiresAt <= now
                || (cached.Key.HostId == active.HostId && cached.Key != active)
            )
            {
                _ = _cache.TryRemove(cached.Key, out _);
            }
        }
    }

    private sealed record CacheKey(int HostId, string BotUserId);

    private sealed record CacheEntry(
        ImmutableArray<HelixChatter> Chatters,
        DateTimeOffset ExpiresAt
    );
}

internal sealed class UnavailableMessageLibraryChatterSource : IMessageLibraryChatterSource
{
    public Task<ImmutableArray<HelixChatter>> GetAsync(
        MessageLibraryRenderHost host,
        CancellationToken cancellationToken
    ) => Task.FromResult(ImmutableArray<HelixChatter>.Empty);
}
