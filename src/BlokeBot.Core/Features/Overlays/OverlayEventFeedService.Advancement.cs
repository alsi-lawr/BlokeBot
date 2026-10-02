using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayEventFeedService
{
    private Task<bool> PruneAndAdvanceAsync(
        BlokeBotDbContext db,
        OverlayInstance overlay,
        CancellationToken ct
    ) => PruneAndAdvanceAsync(db, FeedOwner.From(overlay), ct);

    private async Task<bool> PruneAndAdvanceAsync(
        BlokeBotDbContext db,
        FeedOwner overlay,
        CancellationToken ct
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        _ = await db
            .OverlayEventFeedItems.Where(x =>
                x.OverlayInstanceId == overlay.SimpleId
                && x.FullOverlayEventFeedBindingId == overlay.FullId
                && x.TombstoneExpiresAtUtc != null
                && x.TombstoneExpiresAtUtc <= now
            )
            .ExecuteDeleteAsync(ct);
        var changed = await RecoverUnavailableSourcesAsync(db, overlay, now, ct) > 0;
        var active = await db.OverlayEventFeedItems.SingleOrDefaultAsync(
            x =>
                x.OverlayInstanceId == overlay.SimpleId
                && x.FullOverlayEventFeedBindingId == overlay.FullId
                && x.Lifecycle == OverlayEventFeedLifecycle.Active,
            ct
        );
        if (active is not null && active.DisplayDeadlineUtc > now)
        {
            return changed;
        }
        if (active is not null)
        {
            active.Lifecycle = OverlayEventFeedLifecycle.Consumed;
            active.DisplayDeadlineUtc = null;
            active.TombstoneExpiresAtUtc = now.Add(_tombstoneRetention);
            _ = await db.SaveChangesAsync(ct);
            changed = true;
        }
        var queued = await db
            .OverlayEventFeedItems.Where(x =>
                x.OverlayInstanceId == overlay.SimpleId
                && x.FullOverlayEventFeedBindingId == overlay.FullId
                && x.Lifecycle == OverlayEventFeedLifecycle.Queued
            )
            .OrderBy(x => x.EnqueuedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        if (queued.Count == 0)
        {
            _ = await db.SaveChangesAsync(ct);
            return changed;
        }
        var recent = await db
            .OverlayEventFeedItems.AsNoTracking()
            .Where(x =>
                x.OverlayInstanceId == overlay.SimpleId
                && x.FullOverlayEventFeedBindingId == overlay.FullId
                && x.Lifecycle == OverlayEventFeedLifecycle.Consumed
            )
            .OrderByDescending(x => x.TombstoneExpiresAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(3)
            .Select(x => x.Priority)
            .ToListAsync(ct);
        var forceNormal =
            queued.Any(x => x.Priority == OverlayEventFeedPriority.Normal)
            && recent.Count == 3
            && recent.All(x => x == OverlayEventFeedPriority.High);
        var next =
            queued.FirstOrDefault(x =>
                x.Priority
                == (forceNormal ? OverlayEventFeedPriority.Normal : OverlayEventFeedPriority.High)
            ) ?? queued[0];
        next.Lifecycle = OverlayEventFeedLifecycle.Active;
        next.DisplayDeadlineUtc = now.AddSeconds(next.DurationSeconds);
        _ = await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<int> RecoverUnavailableSourcesAsync(
        BlokeBotDbContext db,
        FeedOwner overlay,
        DateTime now,
        CancellationToken ct
    )
    {
        var enabledFeatures = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == overlay.HostId)
            .Select(x => x.EnabledFeatures)
            .SingleAsync(ct);
        var activeOrQueued = db.OverlayEventFeedItems.Where(x =>
            x.OverlayInstanceId == overlay.SimpleId
            && x.FullOverlayEventFeedBindingId == overlay.FullId
            && (
                x.Lifecycle == OverlayEventFeedLifecycle.Active
                || x.Lifecycle == OverlayEventFeedLifecycle.Queued
            )
        );
        if ((enabledFeatures & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays)
        {
            return await SuppressRecoveredAsync(activeOrQueued, now, ct);
        }
        var suppressed = 0;
        if ((enabledFeatures & HostFeatureFlags.Points) != HostFeatureFlags.Points)
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x =>
                    x.Kind == OverlayEventFeedKind.PointAward
                    || x.Kind == OverlayEventFeedKind.GiveawayWinner
                ),
                now,
                ct
            );
        }
        if ((enabledFeatures & HostFeatureFlags.Guessing) != HostFeatureFlags.Guessing)
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x => x.Kind == OverlayEventFeedKind.GuessingWinner),
                now,
                ct
            );
        }
        if ((enabledFeatures & HostFeatureFlags.Bingo) != HostFeatureFlags.Bingo)
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x => x.Kind == OverlayEventFeedKind.BingoEvent),
                now,
                ct
            );
        }
        if (
            (enabledFeatures & HostFeatureFlags.CommunityProgression)
            != HostFeatureFlags.CommunityProgression
        )
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x => x.Kind == OverlayEventFeedKind.AchievementCompletion),
                now,
                ct
            );
        }
        return suppressed;
    }

    private static Task<int> SuppressRecoveredAsync(
        IQueryable<OverlayEventFeedItem> query,
        DateTime now,
        CancellationToken ct
    ) =>
        query.ExecuteUpdateAsync(
            setters =>
                setters
                    .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                    .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                    .SetProperty(x => x.TombstoneExpiresAtUtc, now.Add(_tombstoneRetention)),
            ct
        );
}
