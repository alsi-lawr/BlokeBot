using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.WatchTime;

internal static class WatchTimeHostQueries
{
    internal static IQueryable<WatchTimeHostSnapshot> Snapshot(
        BlokeBotDbContext db,
        int? hostId = null,
        bool enabledOnly = false
    ) =>
        from host in db.Hosts.AsNoTracking()
        join settings in db.PointsSettings.AsNoTracking() on host.Id equals settings.HostId
        join account in db.HostBotAccountSettings.AsNoTracking()
            on host.Id equals account.HostId
            into accounts
        from account in accounts.DefaultIfEmpty()
        where
            (hostId == null || host.Id == hostId)
            && (
                !enabledOnly
                || (
                    settings.WatchTimePointsEnabled
                    && (host.EnabledFeatures & BlokeBot.Persistence.Models.HostFeatureFlags.Points)
                        == BlokeBot.Persistence.Models.HostFeatureFlags.Points
                )
            )
        select new WatchTimeHostSnapshot(
            host.Id,
            host.Login,
            host.TwitchUserId,
            host.EnabledFeatures,
            host.BotRuntimeState,
            settings.WatchTimePointsEnabled,
            settings.WatchTimePointAmount,
            settings.WatchTimeConfigurationRevision,
            settings.WatchTimeEnableGeneration,
            account != null && account.OverrideEnabled,
            account == null ? null : account.TwitchUserId,
            account == null ? null : account.Login
        );
}
