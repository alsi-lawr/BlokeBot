using BlokeBot.AppEvents;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostConfig.Access;

public sealed class HostModAccessService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AppEventBus events
)
{
    public async Task AddEntryAsync(int hostId, string kind, string login, CancellationToken ct)
    {
        var normalized = LoginName.Parse(login);
        if (normalized.IsEmpty)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSettingsAsync(db, hostId, ct);
        if (
            await db.HostModAccessEntries.AnyAsync(
                x => x.HostId == hostId && x.Kind == kind && x.Login == normalized.Value,
                ct
            )
        )
            return;

        db.HostModAccessEntries.Add(
            new HostModAccessEntry
            {
                CreatedAtUtc = DateTime.UtcNow,
                HostId = hostId,
                Kind = kind,
                Login = normalized.Value,
            }
        );
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.HostedChannelsChanged);
    }

    public async Task<bool> CanModeratorAccessAsync(int hostId, string login, CancellationToken ct)
    {
        var normalized = LoginName.Parse(login);
        if (normalized.IsEmpty)
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (!settings.ModsEnabled)
            return false;

        if (
            await db.HostModAccessEntries.AnyAsync(
                x =>
                    x.HostId == hostId
                    && x.Kind == HostModAccessEntryKind.Blacklist
                    && x.Login == normalized.Value,
                ct
            )
        )
        {
            return false;
        }

        var whitelist = db.HostModAccessEntries.Where(x =>
            x.HostId == hostId && x.Kind == HostModAccessEntryKind.Whitelist
        );
        return !await whitelist.AnyAsync(ct)
            || await whitelist.AnyAsync(x => x.Login == normalized.Value, ct);
    }

    public async Task<HostModAccessState> LoadAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        var entries = await db
            .HostModAccessEntries.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderBy(x => x.Login)
            .ToListAsync(ct);

        return new HostModAccessState(
            settings.ModsEnabled,
            entries
                .Where(x => x.Kind == HostModAccessEntryKind.Whitelist)
                .Select(x => x.Login)
                .ToArray(),
            entries
                .Where(x => x.Kind == HostModAccessEntryKind.Blacklist)
                .Select(x => x.Login)
                .ToArray()
        );
    }

    public async Task RemoveEntryAsync(int hostId, string kind, string login, CancellationToken ct)
    {
        var normalized = LoginName.Parse(login);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deleted = await db
            .HostModAccessEntries.Where(x =>
                x.HostId == hostId && x.Kind == kind && x.Login == normalized.Value
            )
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
            await events.PublishAsync(AppEventKind.HostedChannelsChanged);
    }

    public async Task SetModsEnabledAsync(int hostId, bool enabled, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        settings.ModsEnabled = enabled;
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.HostedChannelsChanged);
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
