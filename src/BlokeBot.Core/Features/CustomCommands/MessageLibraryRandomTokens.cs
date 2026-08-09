using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
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

internal abstract record MessageLibraryRandomToken
{
    private MessageLibraryRandomToken() { }

    internal sealed record From(ImmutableArray<string> Values) : MessageLibraryRandomToken;

    internal sealed record Between(int Minimum, int Maximum) : MessageLibraryRandomToken;

    internal sealed record Viewer : MessageLibraryRandomToken;
}

internal static class MessageLibraryRandomTokenParser
{
    public static string? Validate(string template)
    {
        for (
            var start = template.IndexOf('{');
            start >= 0;
            start = template.IndexOf('{', start + 1)
        )
        {
            var end = template.IndexOf('}', start + 1);
            if (end < 0)
            {
                return IsRecognizedPrefix(template.AsSpan(start + 1))
                    ? "Random message tokens need a closing brace."
                    : null;
            }

            _ = TryParse(template[(start + 1)..end], out _, out var error, out var recognized);
            if (recognized && error is not null)
            {
                return error;
            }

            start = end;
        }

        return null;
    }

    public static bool TryParse(
        string value,
        out MessageLibraryRandomToken? token,
        out string? error,
        out bool recognized
    )
    {
        token = null;
        error = null;
        var parts = value.Split('|');
        recognized =
            IsName(parts[0], "random_from")
            || IsName(parts[0], "random_between")
            || IsName(parts[0], "random_viewer");
        if (!recognized)
        {
            return false;
        }

        if (value.Contains('{', StringComparison.Ordinal))
        {
            error = "Random message values cannot contain braces.";
            return false;
        }

        if (IsName(parts[0], "random_from"))
        {
            var values = parts.Skip(1).Select(static item => item.Trim()).ToImmutableArray();
            if (values.Length == 0 || values.Any(string.IsNullOrEmpty))
            {
                error = "random_from needs at least one non-empty value.";
                return false;
            }

            token = new MessageLibraryRandomToken.From(values);
            return true;
        }

        if (IsName(parts[0], "random_between"))
        {
            if (
                parts.Length != 3
                || !int.TryParse(
                    parts[1].Trim(),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var minimum
                )
                || !int.TryParse(
                    parts[2].Trim(),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var maximum
                )
            )
            {
                error = "random_between needs exactly two whole numbers.";
                return false;
            }

            if (minimum > maximum)
            {
                error = "random_between needs the lower number first.";
                return false;
            }

            token = new MessageLibraryRandomToken.Between(minimum, maximum);
            return true;
        }

        if (parts.Length != 1)
        {
            error = "random_viewer does not take values.";
            return false;
        }

        token = new MessageLibraryRandomToken.Viewer();
        return true;
    }

    private static bool IsRecognizedPrefix(ReadOnlySpan<char> value) =>
        IsTokenNameOrValue(value, "random_from")
        || IsTokenNameOrValue(value, "random_between")
        || IsTokenNameOrValue(value, "random_viewer");

    private static bool IsTokenNameOrValue(ReadOnlySpan<char> value, ReadOnlySpan<char> name) =>
        value.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (
            value.Length > name.Length
            && value[name.Length] == '|'
            && value[..name.Length].Equals(name, StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsName(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
