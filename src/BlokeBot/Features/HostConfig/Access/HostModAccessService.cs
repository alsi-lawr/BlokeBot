using BlokeBot.Features.AccessLists;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostConfig.Access;

public sealed class HostModAccessService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes
)
{
    public async Task AddEntryAsync(
        int hostId,
        AccessListEntryKind kind,
        string login,
        CancellationToken ct
    )
    {
        if (!AccessListStore.TryNormalizeLogin(login, out var normalized))
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSettingsAsync(db, hostId, ct);
        var added = await AccessListStore.AddNormalizedAsync(
            db.HostModAccessEntries,
            db.HostModAccessEntries.Where(x => x.HostId == hostId),
            kind,
            normalized,
            normalizedLogin => new HostModAccessEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                HostId = hostId,
                Kind = kind,
                Login = normalizedLogin,
            },
            ct
        );
        if (!added)
            return;

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public async Task<bool> CanModeratorAccessAsync(int hostId, string login, CancellationToken ct)
    {
        if (!AccessListStore.TryNormalizeLogin(login, out var normalized))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        var accessList = await AccessListStore.LoadAsync(
            db.HostModAccessEntries.Where(x => x.HostId == hostId),
            ct
        );
        return accessList.Allows(
            normalized,
            new AccessListPolicy(
                settings.ModsEnabled,
                AccessListWhitelistMode.RequiredWhenEntriesExist
            )
        );
    }

    public async Task<HostModAccessState> LoadAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        var accessList = await AccessListStore.LoadAsync(
            db.HostModAccessEntries.Where(x => x.HostId == hostId),
            ct
        );

        return new HostModAccessState(
            settings.ModsEnabled,
            accessList.Whitelist,
            accessList.Blacklist
        );
    }

    public async Task RemoveEntryAsync(
        int hostId,
        AccessListEntryKind kind,
        string login,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deleted = await AccessListStore.RemoveAsync(
            db.HostModAccessEntries.Where(x => x.HostId == hostId),
            kind,
            login,
            ct
        );
        if (deleted > 0)
            await changes.NotifyChangedAsync();
    }

    public async Task SetModsEnabledAsync(int hostId, bool enabled, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        settings.ModsEnabled = enabled;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public static async Task<HostModAccessSettings> EnsureSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var settings = await db.HostModAccessSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        if (settings is not null)
            return settings;

        settings = new HostModAccessSettings { HostId = hostId, ModsEnabled = true };
        db.HostModAccessSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }
}
