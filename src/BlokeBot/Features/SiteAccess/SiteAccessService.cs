using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Identity;
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
    public async Task AddEntryAsync(string kind, string login, CancellationToken ct)
    {
        var normalized = LoginName.Parse(login);
        if (normalized.IsEmpty)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSettingsAsync(db, ct);
        if (
            await db.SiteAccessEntries.AnyAsync(
                x => x.Kind == kind && x.Login == normalized.Value,
                ct
            )
        )
            return;

        db.SiteAccessEntries.Add(
            new SiteAccessEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                Kind = kind,
                Login = normalized.Value,
            }
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public async Task<bool> CanCreateHostAsync(string login, CancellationToken ct)
    {
        if (admins.IsAdmin(login))
            return true;

        var normalized = LoginName.Parse(login);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, ct);

        if (
            await db.SiteAccessEntries.AnyAsync(
                x => x.Kind == SiteAccessEntryKind.Blacklist && x.Login == normalized.Value,
                ct
            )
        )
            return false;

        return !settings.WhitelistEnabled
            || await db.SiteAccessEntries.AnyAsync(
                x => x.Kind == SiteAccessEntryKind.Whitelist && x.Login == normalized.Value,
                ct
            );
    }

    public async Task<SiteAccessAdminState> LoadAdminStateAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, ct);
        var entries = await db
            .SiteAccessEntries.AsNoTracking()
            .OrderBy(x => x.Login)
            .ToListAsync(ct);

        return new SiteAccessAdminState(
            settings.WhitelistEnabled,
            entries
                .Where(x => x.Kind == SiteAccessEntryKind.Whitelist)
                .Select(x => x.Login)
                .ToArray(),
            entries
                .Where(x => x.Kind == SiteAccessEntryKind.Blacklist)
                .Select(x => x.Login)
                .ToArray()
        );
    }

    public async Task RemoveEntryAsync(string kind, string login, CancellationToken ct)
    {
        var normalized = LoginName.Parse(login);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db
            .SiteAccessEntries.Where(x => x.Kind == kind && x.Login == normalized.Value)
            .ExecuteDeleteAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public async Task SetWhitelistEnabledAsync(bool enabled, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, ct);
        settings.WhitelistEnabled = enabled;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    private static async Task<SiteAccessSettings> EnsureSettingsAsync(
        BlokeBotDbContext db,
        CancellationToken ct
    )
    {
        var settings = await db.SiteAccessSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is not null)
            return settings;

        settings = new SiteAccessSettings { Id = 1, WhitelistEnabled = false };
        db.SiteAccessSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }
}
