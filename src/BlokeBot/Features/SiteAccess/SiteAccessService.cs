using BlokeBot.Features.AccessLists;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.SiteAccess;

public sealed class SiteAccessService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    BotAdminService admins,
    SiteAccessChangeNotifier changes
)
{
    public async Task AddEntryAsync(AccessListEntryKind kind, string login, CancellationToken ct)
    {
        var normalized = AccessListStore
            .NormalizeLogin(login)
            .Match<string?>(value => value, _ => null);
        if (normalized is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSettingsAsync(db, ct);
        var changed = await AccessListStore.AddNormalizedAsync(
            db.SiteAccessEntries,
            db.SiteAccessEntries,
            kind,
            normalized,
            normalizedLogin => new SiteAccessEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                Kind = kind,
                Login = normalizedLogin,
            },
            ct
        );
        if (!changed)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    public async Task<bool> CanCreateHostAsync(string login, CancellationToken ct)
    {
        if (admins.IsAdmin(login))
        {
            return true;
        }

        var normalized = AccessListStore
            .NormalizeLogin(login)
            .Match<string?>(value => value, _ => null);
        if (normalized is null)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, ct);
        var accessList = await AccessListStore.LoadAsync(db.SiteAccessEntries, ct);

        return accessList.Allows(
            normalized,
            new AccessListPolicy(
                Enabled: true,
                WhitelistMode: settings.WhitelistEnabled
                    ? AccessListWhitelistMode.Required
                    : AccessListWhitelistMode.Disabled
            )
        );
    }

    public async Task<SiteAccessAdminState> LoadAdminStateAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, ct);
        var accessList = await AccessListStore.LoadAsync(db.SiteAccessEntries, ct);

        return new SiteAccessAdminState(
            settings.WhitelistEnabled,
            accessList.Whitelist,
            accessList.Blacklist
        );
    }

    public async Task RemoveEntryAsync(AccessListEntryKind kind, string login, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await AccessListStore.RemoveAsync(db.SiteAccessEntries, kind, login, ct);
        await changes.NotifyChangedAsync(ct);
    }

    public async Task SetWhitelistEnabledAsync(bool enabled, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, ct);
        settings.WhitelistEnabled = enabled;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    private static async Task<SiteAccessSettings> EnsureSettingsAsync(
        BlokeBotDbContext db,
        CancellationToken ct
    )
    {
        var settings = await db.SiteAccessSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new SiteAccessSettings { Id = 1, WhitelistEnabled = false };
        db.SiteAccessSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }
}
