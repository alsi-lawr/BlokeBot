using BlokeBot.Eventing;
using BlokeBot.Features.AccessLists;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
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
        var normalized = AccessListStore
            .NormalizeLogin(login)
            .Match<string?>(value => value, _ => null);
        if (normalized is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSettingsAsync(db, hostId, ct);
        var changed = await AccessListStore.AddNormalizedAsync(
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
        if (!changed)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    public async Task<bool> CanModeratorAccessAsync(int hostId, string login, CancellationToken ct)
    {
        var normalized = AccessListStore
            .NormalizeLogin(login)
            .Match<string?>(value => value, _ => null);
        if (normalized is null)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        var accessList = await AccessListStore.LoadAsync(
            db.HostModAccessEntries.Where(x => x.HostId == hostId),
            ct
        );
        return accessList.Allows(
            normalized,
            !settings.ModsEnabled ? new AccessListPolicy.Disabled()
                : settings.AllowModsByDefault ? new AccessListPolicy.BlacklistByDefault()
                : new AccessListPolicy.WhitelistRequired()
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
            settings.AllowModsByDefault,
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
        {
            await changes.NotifyChangedAsync(ct);
        }
    }

    public Task EnableModeratorAccessAsync(int hostId, CancellationToken ct)
    {
        return UpdateSettingsAsync(hostId, static settings => settings.ModsEnabled = true, ct);
    }

    public Task DisableModeratorAccessAsync(int hostId, CancellationToken ct)
    {
        return UpdateSettingsAsync(hostId, static settings => settings.ModsEnabled = false, ct);
    }

    public IO<HostModAccessSaved, HostModAccessSaveFailure> SaveModeratorAccess(
        HostModAccessSaveCommand command
    )
    {
        return IO<HostModAccessSaved, HostModAccessSaveFailure>.Create(ct =>
            ExecuteSaveModeratorAccessAsync(command, ct)
        );
    }

    private async ValueTask<
        Result<HostModAccessSaved, HostModAccessSaveFailure>
    > ExecuteSaveModeratorAccessAsync(HostModAccessSaveCommand command, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(host => host.Id == command.HostId, ct))
        {
            return Result<HostModAccessSaved, HostModAccessSaveFailure>.Error(
                new HostModAccessSaveFailure.HostNotFound()
            );
        }

        var settings = await db.HostModAccessSettings.SingleOrDefaultAsync(
            x => x.HostId == command.HostId,
            ct
        );
        var settingsExisted = settings is not null;
        if (settings is null)
        {
            settings = new HostModAccessSettings
            {
                HostId = command.HostId,
                ModsEnabled = true,
                AllowModsByDefault = true,
            };
            db.HostModAccessSettings.Add(settings);
        }

        var previousAllowModsByDefault = settings.AllowModsByDefault;
        settings.AllowModsByDefault = command.Mode.AllowModsByDefault;
        await db.SaveChangesAsync(ct);

        var notification = await changes.NotifyChangedAsync(CancellationToken.None);
        if (notification is ObserverFanOutOutcome.AllSucceeded notified)
        {
            return Result<HostModAccessSaved, HostModAccessSaveFailure>.Success(
                new(command.HostId, command.Mode, notified.ObserverCount)
            );
        }

        var failedNotification = (ObserverFanOutOutcome.CompletedWithFailures)notification;
        if (settingsExisted)
        {
            settings.AllowModsByDefault = previousAllowModsByDefault;
        }
        else
        {
            db.HostModAccessSettings.Remove(settings);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        var rollbackNotification = await changes.NotifyChangedAsync(CancellationToken.None);
        var failedRollbackObserverCount = rollbackNotification switch
        {
            ObserverFanOutOutcome.AllSucceeded => 0,
            ObserverFanOutOutcome.CompletedWithFailures failed => failed.Failures.Count,
            _ => throw new InvalidOperationException("Unknown observer fan-out outcome."),
        };
        return Result<HostModAccessSaved, HostModAccessSaveFailure>.Error(
            new HostModAccessSaveFailure.RuntimeNotificationFailed(
                failedNotification.Failures.Count,
                failedRollbackObserverCount
            )
        );
    }

    private async Task UpdateSettingsAsync(
        int hostId,
        Action<HostModAccessSettings> update,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        update(settings);
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
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
        {
            return settings;
        }

        settings = new HostModAccessSettings
        {
            HostId = hostId,
            ModsEnabled = true,
            AllowModsByDefault = true,
        };
        db.HostModAccessSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }
}
