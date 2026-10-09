using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayEventFeedService
{
    private async Task AdmitAsync(OverlayEventPresentation presentation, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var required = SourceFeature(presentation.Kind) | HostFeatureFlags.Overlays;
        if (!await RequiredFeaturesEnabledAsync(db, presentation.HostId, required, ct))
        {
            return;
        }
        var overlays = await db
            .OverlayInstances.Where(x =>
                x.HostId == presentation.HostId && x.Type == OverlayType.EventFeed && x.IsEnabled
            )
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        var owners = overlays
            .Select(FeedOwner.From)
            .Concat(await ReconcileFullAsync(db, presentation.HostId, ct));
        foreach (var overlay in owners)
        {
            var configuration = overlay.Configuration;
            var kindConfiguration = configuration.Kinds[presentation.Kind];
            if (!kindConfiguration.Enabled)
            {
                continue;
            }
            _ = await PruneAndAdvanceAsync(db, overlay, ct);
            if (
                await db.OverlayEventFeedItems.AnyAsync(
                    x =>
                        x.OverlayInstanceId == overlay.SimpleId
                        && x.FullOverlayEventFeedBindingId == overlay.FullId
                        && x.Kind == presentation.Kind
                        && x.SourceKey == presentation.SourceKey,
                    ct
                )
            )
            {
                continue;
            }
            var count = await db.OverlayEventFeedItems.CountAsync(
                x =>
                    x.OverlayInstanceId == overlay.SimpleId
                    && x.FullOverlayEventFeedBindingId == overlay.FullId
                    && (
                        x.Lifecycle == OverlayEventFeedLifecycle.Active
                        || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                    ),
                ct
            );
            if (count >= configuration.Capacity)
            {
                if (configuration.OverflowPolicy == EventFeedOverflowPolicy.DropNewest)
                {
                    continue;
                }
                var replaced = await db
                    .OverlayEventFeedItems.Where(x =>
                        x.OverlayInstanceId == overlay.SimpleId
                        && x.FullOverlayEventFeedBindingId == overlay.FullId
                        && x.Kind == presentation.Kind
                        && x.Lifecycle == OverlayEventFeedLifecycle.Queued
                    )
                    .OrderByDescending(x => x.EnqueuedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);
                if (replaced is null)
                {
                    continue;
                }
                replaced.Lifecycle = OverlayEventFeedLifecycle.Suppressed;
                replaced.TombstoneExpiresAtUtc = timeProvider
                    .GetUtcNow()
                    .UtcDateTime.Add(_tombstoneRetention);
            }
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var hasActive = await db.OverlayEventFeedItems.AnyAsync(
                x =>
                    x.OverlayInstanceId == overlay.SimpleId
                    && x.FullOverlayEventFeedBindingId == overlay.FullId
                    && x.Lifecycle == OverlayEventFeedLifecycle.Active,
                ct
            );
            _ = db.OverlayEventFeedItems.Add(
                new OverlayEventFeedItem
                {
                    OverlayInstanceId = overlay.SimpleId,
                    FullOverlayEventFeedBindingId = overlay.FullId,
                    HostId = overlay.HostId,
                    Kind = presentation.Kind,
                    SourceKey = presentation.SourceKey,
                    Priority = kindConfiguration.Priority,
                    Lifecycle = hasActive
                        ? OverlayEventFeedLifecycle.Queued
                        : OverlayEventFeedLifecycle.Active,
                    Title = Title(presentation.Kind),
                    Body = EventFeedTemplateRenderer.Render(kindConfiguration, presentation),
                    DurationSeconds = kindConfiguration.DurationSeconds,
                    EnqueuedAtUtc = now,
                    DisplayDeadlineUtc = hasActive
                        ? null
                        : now.AddSeconds(kindConfiguration.DurationSeconds),
                }
            );
            _ = await db.SaveChangesAsync(ct);
            await PublishStateAsync(overlay);
        }
    }
}
