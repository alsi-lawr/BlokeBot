using BlokeBot.Identity;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.AccessLists;

internal enum AccessListWhitelistMode
{
    Disabled,
    Required,
    RequiredWhenEntriesExist,
}

internal sealed record AccessListPolicy(bool Enabled, AccessListWhitelistMode WhitelistMode);

internal sealed record AccessListEntryValue(AccessListEntryKind Kind, string Login);

internal sealed record AccessListSnapshot(string[] Whitelist, string[] Blacklist)
{
    public bool Allows(string normalizedLogin, AccessListPolicy policy)
    {
        if (!policy.Enabled)
            return false;

        if (Blacklist.Contains(normalizedLogin, StringComparer.OrdinalIgnoreCase))
            return false;

        return policy.WhitelistMode switch
        {
            AccessListWhitelistMode.Disabled => true,
            AccessListWhitelistMode.Required => Whitelist.Contains(
                normalizedLogin,
                StringComparer.OrdinalIgnoreCase
            ),
            AccessListWhitelistMode.RequiredWhenEntriesExist => Whitelist.Length == 0
                || Whitelist.Contains(normalizedLogin, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.WhitelistMode, null),
        };
    }

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
            return false;

        entries.Add(createEntry(normalizedLogin));
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
        if (!TryNormalizeLogin(login, out var normalized))
            return 0;

        return await scopedEntries
            .Where(x => x.Kind == kind && x.Login == normalized)
            .ExecuteDeleteAsync(ct);
    }

    public static bool TryNormalizeLogin(string login, out string normalized)
    {
        var parsed = LoginName.Parse(login);
        normalized = parsed.Value;
        return !parsed.IsEmpty;
    }
}
