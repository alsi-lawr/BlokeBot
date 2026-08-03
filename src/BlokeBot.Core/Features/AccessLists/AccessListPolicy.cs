using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.AccessLists;

internal abstract record AccessListPolicy
{
    private AccessListPolicy() { }

    internal abstract TResult Match<TResult>(
        Func<Disabled, TResult> disabled,
        Func<BlacklistByDefault, TResult> blacklistByDefault,
        Func<WhitelistRequired, TResult> whitelistRequired
    );

    internal sealed record Disabled : AccessListPolicy
    {
        internal override TResult Match<TResult>(
            Func<Disabled, TResult> disabled,
            Func<BlacklistByDefault, TResult> blacklistByDefault,
            Func<WhitelistRequired, TResult> whitelistRequired
        ) => disabled(this);
    }

    internal sealed record BlacklistByDefault : AccessListPolicy
    {
        internal override TResult Match<TResult>(
            Func<Disabled, TResult> disabled,
            Func<BlacklistByDefault, TResult> blacklistByDefault,
            Func<WhitelistRequired, TResult> whitelistRequired
        ) => blacklistByDefault(this);
    }

    internal sealed record WhitelistRequired : AccessListPolicy
    {
        internal override TResult Match<TResult>(
            Func<Disabled, TResult> disabled,
            Func<BlacklistByDefault, TResult> blacklistByDefault,
            Func<WhitelistRequired, TResult> whitelistRequired
        ) => whitelistRequired(this);
    }
}

internal sealed record AccessListEntryValue(AccessListEntryKind Kind, string Login);

internal sealed record AccessListSnapshot(string[] Whitelist, string[] Blacklist)
{
    public bool Allows(string normalizedLogin, AccessListPolicy policy) =>
        policy.Match(
            _ => false,
            _ => !Blacklist.Contains(normalizedLogin, StringComparer.OrdinalIgnoreCase),
            _ => Whitelist.Contains(normalizedLogin, StringComparer.OrdinalIgnoreCase)
        );

    public static AccessListSnapshot From(IEnumerable<AccessListEntryValue> entries)
    {
        var ordered = entries.OrderBy(x => x.Login, StringComparer.OrdinalIgnoreCase).ToArray();
        return new AccessListSnapshot(
            ordered
                .Where(x => x.Kind == AccessListEntryKind.Whitelist)
                .Select(x => x.Login)
                .ToArray(),
            ordered
                .Where(x => x.Kind == AccessListEntryKind.Blacklist)
                .Select(x => x.Login)
                .ToArray()
        );
    }
}

internal static class AccessListStore
{
    public static async Task<bool> AddNormalizedAsync<TEntry>(
        DbSet<TEntry> entries,
        IQueryable<TEntry> scopedEntries,
        AccessListEntryKind kind,
        string normalizedLogin,
        Func<string, TEntry> createEntry,
        CancellationToken ct
    )
        where TEntry : class, IAccessListEntry
    {
        if (await scopedEntries.AnyAsync(x => x.Kind == kind && x.Login == normalizedLogin, ct))
        {
            return false;
        }

        _ = entries.Add(createEntry(normalizedLogin));
        return true;
    }

    public static async Task<AccessListSnapshot> LoadAsync<TEntry>(
        IQueryable<TEntry> scopedEntries,
        CancellationToken ct
    )
        where TEntry : class, IAccessListEntry
    {
        var entries = await scopedEntries
            .AsNoTracking()
            .OrderBy(x => x.Login)
            .Select(x => new AccessListEntryValue(x.Kind, x.Login))
            .ToListAsync(ct);

        return AccessListSnapshot.From(entries);
    }

    public static async Task<int> RemoveAsync<TEntry>(
        IQueryable<TEntry> scopedEntries,
        AccessListEntryKind kind,
        string login,
        CancellationToken ct
    )
        where TEntry : class, IAccessListEntry
    {
        var normalized = NormalizeLogin(login).Match<string?>(value => value, _ => null);
        return normalized is null
            ? 0
            : await scopedEntries
                .Where(x => x.Kind == kind && x.Login == normalized)
                .ExecuteDeleteAsync(ct);
    }

    public static Result<string, AccessListLoginNormalizationFailure> NormalizeLogin(string login)
    {
        var parsed = LoginName.Parse(login);
        return parsed.IsEmpty
            ? Result<string, AccessListLoginNormalizationFailure>.Error(
                new AccessListLoginNormalizationFailure()
            )
            : Result<string, AccessListLoginNormalizationFailure>.Success(parsed.Value);
    }
}

internal readonly record struct AccessListLoginNormalizationFailure;
