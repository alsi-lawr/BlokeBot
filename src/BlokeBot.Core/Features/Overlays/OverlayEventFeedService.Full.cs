using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayEventFeedService
{
    private sealed record FeedOwner(
        int HostId,
        long? SimpleId,
        long? FullId,
        Guid PublicId,
        Guid? BindingId,
        OverlayConfiguration.EventFeedV1 Configuration,
        ResolvedOverlayInstance? Simple
    )
    {
        internal static FeedOwner From(OverlayInstance overlay)
        {
            var configuration = (OverlayConfiguration.EventFeedV1)
                OverlayConfiguration.FromPersistence(overlay.Type, overlay.ConfigurationJson);
            return new(
                overlay.HostId,
                overlay.Id,
                null,
                overlay.PublicId,
                null,
                configuration,
                ToResolved(overlay, configuration)
            );
        }

        internal static FeedOwner From(FullOverlayEventFeedBinding binding) =>
            new(
                binding.HostId,
                null,
                binding.Id,
                binding.FullOverlay.PublicId,
                binding.BindingId,
                (OverlayConfiguration.EventFeedV1)
                    OverlayConfiguration.FromPersistence(
                        OverlayType.EventFeed,
                        binding.ConfigurationJson
                    ),
                null
            );
    }

    private async Task<IReadOnlyList<FeedOwner>> ReconcileFullAsync(
        BlokeBotDbContext db,
        int? hostId,
        CancellationToken ct
    )
    {
        var selected = await (
            from overlay in db.FullOverlays
            join publication in db.FullOverlayPublications
                on new { OverlayId = overlay.Id, Version = overlay.PublishedVersion } equals new
                {
                    publication.OverlayId,
                    Version = (long?)publication.Version,
                }
            where !overlay.IsArchived && (hostId == null || overlay.HostId == hostId)
            select new
            {
                Overlay = overlay,
                publication.DocumentJson,
                publication.Version,
            }
        ).ToArrayAsync(ct);
        var bindings = await db
            .FullOverlayEventFeedBindings.Include(binding => binding.FullOverlay)
            .Where(binding => hostId == null || binding.HostId == hostId)
            .ToListAsync(ct);
        var active = new HashSet<FullOverlayEventFeedBinding>();
        var changed = new HashSet<FullOverlayEventFeedBinding>();
        foreach (var publication in selected)
        {
            var document = FullOverlayDocuments.Deserialize(publication.DocumentJson);
            foreach (var config in FullOverlayEventFeeds.Bindings(document))
            {
                var binding = bindings.FirstOrDefault(binding =>
                    binding.FullOverlayId == publication.Overlay.Id
                    && binding.BindingId == config.BindingId
                );
                var configurationJson = FullOverlayEventFeeds.AdmissionConfiguration(config.Feed);
                if (binding is null)
                {
                    binding = new()
                    {
                        FullOverlayId = publication.Overlay.Id,
                        FullOverlay = publication.Overlay,
                        HostId = publication.Overlay.HostId,
                        BindingId = config.BindingId,
                        ConfigurationJson = configurationJson,
                    };
                    _ = db.FullOverlayEventFeedBindings.Add(binding);
                    bindings.Add(binding);
                }
                else if (!binding.IsEnabled || binding.ConfigurationJson != configurationJson)
                {
                    _ = await SuppressRecoveredAsync(
                        db.OverlayEventFeedItems.Where(item =>
                            item.FullOverlayEventFeedBindingId == binding.Id
                            && (
                                item.Lifecycle == OverlayEventFeedLifecycle.Active
                                || item.Lifecycle == OverlayEventFeedLifecycle.Queued
                            )
                        ),
                        timeProvider.GetUtcNow().UtcDateTime,
                        ct
                    );
                }
                if (
                    !binding.IsEnabled
                    || binding.PublishedVersion != publication.Version
                    || binding.ConfigurationJson != configurationJson
                )
                {
                    _ = changed.Add(binding);
                }
                binding.ConfigurationJson = configurationJson;
                binding.PublishedVersion = publication.Version;
                binding.IsEnabled = true;
                _ = active.Add(binding);
            }
        }
        foreach (
            var binding in bindings.Where(binding => !active.Contains(binding) && binding.IsEnabled)
        )
        {
            binding.IsEnabled = false;
            _ = await SuppressRecoveredAsync(
                db.OverlayEventFeedItems.Where(item =>
                    item.FullOverlayEventFeedBindingId == binding.Id
                    && (
                        item.Lifecycle == OverlayEventFeedLifecycle.Active
                        || item.Lifecycle == OverlayEventFeedLifecycle.Queued
                    )
                ),
                timeProvider.GetUtcNow().UtcDateTime,
                ct
            );
            _ = changed.Add(binding);
        }
        _ = await db.SaveChangesAsync(ct);
        foreach (var binding in changed)
        {
            await PublishStateAsync(FeedOwner.From(binding));
        }
        return active.Select(FeedOwner.From).ToArray();
    }

    internal async Task<EventFeedStatePresentation?> ProjectFullAsync(
        int hostId,
        Guid overlayId,
        Guid bindingId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var enabled = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == hostId)
            .Select(host => host.EnabledFeatures)
            .SingleOrDefaultAsync(ct);
        if ((enabled & HostFeatureFlags.Overlays) == 0)
        {
            return null;
        }
        var binding = await db
            .FullOverlayEventFeedBindings.AsNoTracking()
            .Where(binding =>
                binding.HostId == hostId
                && binding.BindingId == bindingId
                && binding.FullOverlay.PublicId == overlayId
                && binding.IsEnabled
                && !binding.FullOverlay.IsArchived
                && binding.PublishedVersion == binding.FullOverlay.PublishedVersion
            )
            .Select(binding => (long?)binding.Id)
            .SingleOrDefaultAsync(ct);
        if (binding is null)
        {
            return null;
        }
        var items = await db
            .OverlayEventFeedItems.AsNoTracking()
            .Where(item =>
                item.HostId == hostId
                && item.FullOverlayEventFeedBindingId == binding
                && (
                    item.Lifecycle == OverlayEventFeedLifecycle.Active
                    || item.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
            )
            .OrderBy(item => item.EnqueuedAtUtc)
            .ThenBy(item => item.Id)
            .ToArrayAsync(ct);
        var visible = items
            .Where(item => (enabled & SourceFeature(item.Kind)) == SourceFeature(item.Kind))
            .ToArray();
        return new(
            visible
                .Where(item => item.Lifecycle == OverlayEventFeedLifecycle.Active)
                .Select(ToPresentation)
                .SingleOrDefault(),
            visible
                .Where(item => item.Lifecycle == OverlayEventFeedLifecycle.Queued)
                .Select(ToPresentation)
                .ToArray()
        );
    }

    private async Task PublishStateAsync(FeedOwner owner)
    {
        if (owner.Simple is { } simple)
        {
            services.GetRequiredService<IOverlayLivePublisher>().PublishState(simple);
        }
        else if (
            services.GetService<EventBus<FullOverlayEventFeedIdentity>>() is { } publisher
            && owner.BindingId is { } bindingId
        )
        {
            _ = await publisher.PublishAsync(
                new(owner.HostId, owner.PublicId, bindingId),
                CancellationToken.None
            );
        }
    }

    private async Task PublishFullSuppressionAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        foreach (var owner in await ReconcileFullAsync(db, hostId, ct))
        {
            _ = await PruneAndAdvanceAsync(db, owner, ct);
            await PublishStateAsync(owner);
        }
    }
}
