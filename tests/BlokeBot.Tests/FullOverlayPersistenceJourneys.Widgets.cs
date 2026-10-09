using System.Text.Json;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Tests;

public sealed partial class FullOverlayPersistenceJourneys
{
    private sealed class FeedPublisher : IOverlayLivePublisher
    {
        public void PublishState(ResolvedOverlayInstance instance) { }

        public void PublishTest(ResolvedOverlayInstance instance) { }
    }

    private static async Task<long> SeedFeedBeforeWidgetMigrationAsync(
        BlokeBotDbContext db,
        int hostId
    )
    {
        var now = DateTime.UtcNow;
        var feed = new OverlayInstance
        {
            PublicId = Guid.NewGuid(),
            HostId = hostId,
            Name = "Existing feed",
            Type = OverlayType.EventFeed,
            IsEnabled = true,
            ConfigurationJson = OverlayConfiguration.EventFeedV1.Default.ToPersistenceJson(),
            AccessKeyDigest = OverlayAccessKeyDigest.Compute(
                new CryptographicOverlayAccessKeyGenerator().Generate()
            ),
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.OverlayInstances.Add(feed);
        _ = await db.SaveChangesAsync();
        // Deliberately use the released column set so the upgrade proves preservation of old owners.
        _ = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO overlay_event_feed_items
                ("OverlayInstanceId", "HostId", "Kind", "SourceKey", "Priority", "Lifecycle",
                 "Title", "Body", "DurationSeconds", "EnqueuedAtUtc", "DisplayDeadlineUtc")
            VALUES ({feed.Id}, {hostId}, {"pointAward"}, {"before-widget-migration"}, {"normal"}, {"active"},
                    {"Existing"}, {"Retained before migration"}, {5}, {now}, {now.AddMinutes(1)})
            """
        );
        return feed.Id;
    }

    private static async Task EventFeedWidgetJourneyAsync(
        Database factory,
        AuthenticatedSession session,
        FullOverlayService full,
        ServiceProvider provider
    )
    {
        const string SourceKey = "full-widget-source";
        var hostId = session.State.ShouldBeOfType<AuthSessionState.Selected>().Selection.Current.Id;
        var bindingId = Guid.NewGuid();
        using var config = JsonDocument.Parse(
            OverlayConfiguration.EventFeedV1.Default.ToPersistenceJson()
        );
        var widget = new FullOverlayWidget(
            new(Guid.NewGuid()),
            new("event-feed"),
            JsonSerializer.SerializeToElement(new { bindingId, feed = config.RootElement }),
            FullOverlayAuthoringMetadata.Default,
            new(false, 0.7)
        );
        var document = FullOverlayDocument.Blank() with
        {
            Widgets = [widget, widget with { Id = new(Guid.NewGuid()), Audio = new(true, 0.3) }],
        };
        var created = Value(
            await full.CreateAsync(session, new("Durable feed", document), CancellationToken.None)
        ).Overlay;
        var published = Value(
            await full.SaveAndPublishAsync(
                session,
                new(created.Id, created.Revision, created.Name, created.Draft),
                CancellationToken.None
            )
        ).Overlay;
        var presentation = new OverlayEventPresentation.PointAward
        {
            HostId = hostId,
            SourceKey = SourceKey,
            Recipient = "public-viewer",
            Amount = "5",
            PointLabel = "points",
        };
        await using (
            var events = new OverlayEventFeedService(
                factory,
                TimeProvider.System,
                provider,
                NullLogger<OverlayEventFeedService>.Instance
            )
        )
        {
            await events.PresentAsync(presentation, CancellationToken.None);
            var projection = await events.ProjectFullAsync(
                hostId,
                created.Id,
                bindingId,
                CancellationToken.None
            );
            _ = projection.ShouldNotBeNull();
            _ = projection.Active.ShouldNotBeNull();
        }
        await using var restarted = new OverlayEventFeedService(
            factory,
            TimeProvider.System,
            provider,
            NullLogger<OverlayEventFeedService>.Instance
        );
        await restarted.PresentAsync(presentation, CancellationToken.None);
        await using (var db = factory.CreateDbContext())
        {
            (await db.FullOverlayEventFeedBindings.CountAsync()).ShouldBe(1);
            var item = await db.OverlayEventFeedItems.SingleAsync(item =>
                item.FullOverlayEventFeedBindingId != null
            );
            item.OverlayInstanceId.ShouldBeNull();
            item.SourceKey.ShouldBe(SourceKey);
            item.Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Active);
            (
                await db.OverlayEventFeedItems.CountAsync(item => item.SourceKey == SourceKey)
            ).ShouldBe(2);
        }
        var archived = Value(
            await full.ArchiveAsync(
                session,
                new(published.Id, published.Revision),
                CancellationToken.None
            )
        );
        (
            await restarted.ProjectFullAsync(hostId, created.Id, bindingId, CancellationToken.None)
        ).ShouldBeNull();
        await restarted.PresentAsync(
            presentation with
            {
                SourceKey = "after-archive",
            },
            CancellationToken.None
        );
        await using (var db = factory.CreateDbContext())
        {
            (
                await db.OverlayEventFeedItems.SingleAsync(item =>
                    item.FullOverlayEventFeedBindingId != null
                )
            ).Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Suppressed);
        }
        var restored = Value(
            await full.RestoreAsync(
                session,
                new(published.Id, archived.Revision),
                CancellationToken.None
            )
        );
        _ = Value(
            await full.DeleteAsync(
                session,
                new(published.Id, restored.Revision, FullOverlayDeleteConfirmation.Confirmed),
                CancellationToken.None
            )
        );
        await using var deleted = factory.CreateDbContext();
        (await deleted.FullOverlayEventFeedBindings.CountAsync()).ShouldBe(0);
        (
            await deleted.OverlayEventFeedItems.CountAsync(item =>
                item.FullOverlayEventFeedBindingId != null
            )
        ).ShouldBe(0);
        Console.WriteLine(
            $"Full feed shared-binding admission, restart dedupe, non-consuming projection, archive suppression and cascade verified: {deleted.Database.ProviderName}."
        );
    }
}
