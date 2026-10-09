using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayEventFeedService
{
    private async Task SuppressAllAsync(int hostId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var activeOrQueued = db.OverlayEventFeedItems.Where(x =>
                x.HostId == hostId
                && (
                    x.Lifecycle == OverlayEventFeedLifecycle.Active
                    || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
            );
            var affectedOverlayIds = await activeOrQueued
                .Where(x => x.OverlayInstanceId != null)
                .Select(x => x.OverlayInstanceId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            _ = await activeOrQueued.ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                        .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                        .SetProperty(x => x.TombstoneExpiresAtUtc, now.Add(_tombstoneRetention)),
                cancellationToken
            );
            await PublishSuppressedStatesAsync(db, hostId, affectedOverlayIds, cancellationToken);
            await PublishFullSuppressionAsync(db, hostId, cancellationToken);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    internal async Task<EventFeedStatePresentation?> ReadAsync(
        ResolvedOverlayInstance instance,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (
                !await RequiredFeaturesEnabledAsync(
                    db,
                    instance.HostId,
                    HostFeatureFlags.Overlays,
                    cancellationToken
                )
            )
            {
                await SuppressAsync(db, instance, null, cancellationToken);
                return null;
            }
            await AdvanceAsync(db, instance, cancellationToken);
            var overlayId = await db
                .OverlayInstances.Where(x =>
                    x.HostId == instance.HostId && x.PublicId == instance.OverlayId
                )
                .Select(x => x.Id)
                .SingleAsync(cancellationToken);
            return await ReadStateAsync(db, instance.HostId, overlayId, cancellationToken);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    internal async Task SuppressSourceAsync(
        int hostId,
        HostFeatureFlags source,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var kind = source switch
            {
                HostFeatureFlags.Guessing => OverlayEventFeedKind.GuessingWinner,
                HostFeatureFlags.Points => (OverlayEventFeedKind?)null,
                HostFeatureFlags.Bingo => OverlayEventFeedKind.BingoEvent,
                HostFeatureFlags.CommunityProgression => OverlayEventFeedKind.AchievementCompletion,
                _ => null,
            };
            var query = db.OverlayEventFeedItems.Where(x =>
                x.HostId == hostId
                && (
                    x.Lifecycle == OverlayEventFeedLifecycle.Active
                    || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
            );
            if (kind is { } exact)
            {
                query = query.Where(x => x.Kind == exact);
            }
            else if (source == HostFeatureFlags.Points)
            {
                query = query.Where(x =>
                    x.Kind == OverlayEventFeedKind.PointAward
                    || x.Kind == OverlayEventFeedKind.GiveawayWinner
                );
            }
            else
            {
                return;
            }
            var affectedOverlayIds = await query
                .Where(x => x.OverlayInstanceId != null)
                .Select(x => x.OverlayInstanceId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            _ = await query.ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                        .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                        .SetProperty(x => x.TombstoneExpiresAtUtc, now.Add(_tombstoneRetention)),
                cancellationToken
            );
            await PublishSuppressedStatesAsync(db, hostId, affectedOverlayIds, cancellationToken);
            await PublishFullSuppressionAsync(db, hostId, cancellationToken);
        }
        finally
        {
            _ = _gate.Release();
        }
    }
}
