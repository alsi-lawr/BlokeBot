using System.Text.Json;
using BlokeBot.Core;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Tests;

public sealed partial class FullOverlayPersistenceJourneys
{
    private static async Task JourneyAsync(
        Database factory,
        FullOverlayMigrationStart start,
        string releasedMigration
    )
    {
        int hostId;
        OverlayInstance simple;
        long retainedFeedItemOwner;
        var key = new CryptographicOverlayAccessKeyGenerator().Generate();
        if (start == FullOverlayMigrationStart.Released016)
        {
            await using var previous = factory.CreateDbContext();
            await previous.Database.MigrateAsync(releasedMigration);
            (hostId, simple) = await SeedSimpleAsync(previous, key);
            retainedFeedItemOwner = await SeedFeedBeforeWidgetMigrationAsync(previous, hostId);
        }
        else
        {
            await new BlokeBotDatabaseInitializer(factory).InitializeAsync(CancellationToken.None);
            await using var fresh = factory.CreateDbContext();
            (hostId, simple) = await SeedSimpleAsync(fresh, key);
            retainedFeedItemOwner = await SeedFeedBeforeWidgetMigrationAsync(fresh, hostId);
        }
        await new BlokeBotDatabaseInitializer(factory).InitializeAsync(CancellationToken.None);
        await using (var upgraded = factory.CreateDbContext())
        {
            var retained = await upgraded.OverlayInstances.SingleAsync(overlay =>
                overlay.Id == simple.Id
            );
            retained.Id.ShouldBe(simple.Id);
            retained.PublicId.ShouldBe(simple.PublicId);
            retained.AccessKeyDigest.ShouldBe(simple.AccessKeyDigest);
            retained.KeyVersion.ShouldBe(simple.KeyVersion);
            retained.ConfigurationJson.ShouldBe(simple.ConfigurationJson);
            retained.Revision.ShouldBe(simple.Revision);
            var feedItem = await upgraded.OverlayEventFeedItems.SingleAsync();
            feedItem.OverlayInstanceId.ShouldBe(retainedFeedItemOwner);
            feedItem.FullOverlayEventFeedBindingId.ShouldBeNull();
            feedItem.SourceKey.ShouldBe("before-widget-migration");
            feedItem.Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Active);
        }
        var host = new BotHostChoice(hostId, "streamer", "Streamer", AuthRole.Streamer);
        var session = new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = "owner-id",
            Login = "owner",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IOverlayLivePublisher, FeedPublisher>();
        _ = services.AddEventBus<AppEventKind>(
            ObserverBoundary.Named("full-overlay-journey"),
            kind => ObserverEventIdentity.Named(kind.ToString())
        );
        await using var provider = services.BuildServiceProvider();
        var events = provider.GetRequiredService<EventBus<AppEventKind>>();
        var moderator = new Moderator();
        var full = new FullOverlayService(
            factory,
            new(factory, moderator),
            new CryptographicOverlayAccessKeyGenerator(),
            new Admission(),
            events,
            TimeProvider.System
        );
        var document = new FullOverlayDocument(
            Guid.NewGuid(),
            "<unrecognized attr='preserve'><unfinished",
            "@layer arbitrary { :root { --custom: 100vw; } }",
            [Widget(new(false, 0.4)), Widget(new(true, 0.9))],
            []
        );
        var created = Value(
            await full.CreateAsync(session, new("Full", document), CancellationToken.None)
        );
        var selected = Value(
            await full.SaveAndPublishAsync(
                session,
                new(created.Overlay.Id, created.Overlay.Revision, "Full", created.Overlay.Draft),
                CancellationToken.None
            )
        ).Overlay;
        var saved = Value(
            await full.SaveAsync(
                session,
                new(
                    selected.Id,
                    selected.Revision,
                    "Full",
                    selected.Draft with
                    {
                        Html = "<newer-incomplete",
                    }
                ),
                CancellationToken.None
            )
        );
        var restarted = new FullOverlayService(
            factory,
            new(factory, moderator),
            new CryptographicOverlayAccessKeyGenerator(),
            new Admission(),
            events,
            TimeProvider.System
        );
        var reloaded = Value(
            await restarted.GetAsync(session, selected.Id, CancellationToken.None)
        );
        reloaded.Draft.Html.ShouldBe(saved.Draft.Html);
        reloaded.Draft.Css.ShouldBe(document.Css);
        reloaded
            .Draft.Widgets.Select(widget => widget.Id)
            .ShouldBe(created.Overlay.Draft.Widgets.Select(widget => widget.Id));
        reloaded
            .Draft.Widgets.Select(widget => widget.Audio)
            .ShouldBe(document.Widgets.Select(widget => widget.Audio));
        var reader = new FullOverlayPublishedReader(factory);
        (
            await reader.ResolveAsync(created.PrivateAccess.AccessKey, CancellationToken.None)
        )!.Document.Html.ShouldBe(document.Html);
        var duplicate = Value(
            await restarted.DuplicateAsync(
                session,
                new(saved.Id, saved.Revision, "Copy"),
                CancellationToken.None
            )
        );
        duplicate.Overlay.PublishedVersion.ShouldBeNull();
        duplicate.Overlay.Draft.Html.ShouldBe(saved.Draft.Html);
        duplicate
            .Overlay.Draft.Widgets.Select(widget => widget.Audio)
            .ShouldBe(document.Widgets.Select(widget => widget.Audio));
        duplicate.PrivateAccess.AccessKey.ShouldNotBe(created.PrivateAccess.AccessKey);
        var archived = Value(
            await restarted.ArchiveAsync(
                session,
                new(saved.Id, saved.Revision),
                CancellationToken.None
            )
        );
        (
            await reader.ResolveAsync(created.PrivateAccess.AccessKey, CancellationToken.None)
        ).ShouldBeNull();
        var restored = Value(
            await restarted.RestoreAsync(
                session,
                new(saved.Id, archived.Revision),
                CancellationToken.None
            )
        );
        (
            await reader.ResolveAsync(created.PrivateAccess.AccessKey, CancellationToken.None)
        )!.Version.ShouldBe(selected.PublishedVersion!.Value);
        _ = Value(
            await restarted.DeleteAsync(
                session,
                new(saved.Id, restored.Revision, FullOverlayDeleteConfirmation.Confirmed),
                CancellationToken.None
            )
        );
        (
            await reader.ResolveAsync(created.PrivateAccess.AccessKey, CancellationToken.None)
        ).ShouldBeNull();
        using var alerts = new DurableAlertService(factory, TimeProvider.System, events);
        var simpleService = new OverlayInstanceService(
            factory,
            moderator,
            new CryptographicOverlayAccessKeyGenerator(),
            alerts,
            events,
            TimeProvider.System,
            NullLogger<OverlayInstanceService>.Instance
        );
        var configured = (
            await simpleService.ConfigureAsync(
                session,
                new(
                    simple.PublicId,
                    new(simple.Revision),
                    new OverlayConfiguration.GuessingV1(false, 17)
                ),
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlayInstanceResult<OverlayInstanceView>.Succeeded>()
            .Value;
        var liveSimple = (
            await new OverlayInstanceResolver(factory).ResolveAsync(key, CancellationToken.None)
        )
            .ShouldBeOfType<OverlayResolutionResult.Resolved>()
            .Instance;
        liveSimple.OverlayId.ShouldBe(simple.PublicId);
        liveSimple.Configuration.ShouldBe(configured.Configuration);
        liveSimple
            .Configuration.ShouldBeOfType<OverlayConfiguration.GuessingV1>()
            .ResultDurationSeconds.ShouldBe(17);
        await EventFeedWidgetJourneyAsync(factory, session, full, provider);
        await using var final = factory.CreateDbContext();
        (
            await final.OverlayInstances.SingleAsync(overlay => overlay.Id == simple.Id)
        ).AccessKeyDigest.ShouldBe(simple.AccessKeyDigest);
        Console.WriteLine(
            $"Full overlay lifecycle/restart and unchanged simple key/live-save verified: {final.Database.ProviderName}, {start}."
        );
    }

    private static FullOverlayWidget Widget(FullOverlayAudio audio) =>
        new(
            new(Guid.NewGuid()),
            new("cue-player"),
            JsonSerializer.SerializeToElement(new { unknown = "preserved" }),
            FullOverlayAuthoringMetadata.Default,
            audio
        );

    private static T Value<T>(FullOverlayResult<T> result) =>
        result.ShouldBeOfType<FullOverlayResult<T>.Succeeded>().Value;

    private static async Task<(int HostId, OverlayInstance Simple)> SeedSimpleAsync(
        BlokeBotDbContext db,
        string key
    )
    {
        var now = DateTime.UtcNow;
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = now,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var simple = new OverlayInstance
        {
            PublicId = Guid.NewGuid(),
            HostId = host.Id,
            Name = "Simple",
            Type = OverlayType.Guessing,
            IsEnabled = true,
            ConfigurationJson = new OverlayConfiguration.GuessingV1(true, 8).ToPersistenceJson(),
            AccessKeyDigest = OverlayAccessKeyDigest.Compute(key),
            KeyVersion = 1,
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _ = db.OverlayInstances.Add(simple);
        _ = await db.SaveChangesAsync();
        return (host.Id, simple);
    }

    private sealed class Database(DbContextOptions<BlokeBotDbContext> options)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext() => new(options);

        public Task<BlokeBotDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class Moderator : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }

    private sealed class Admission : IFullOverlayPublicationAdmission
    {
        public Task<FullOverlayAdmission> AdmitAsync(
            FullOverlayPublicationCandidate candidate,
            CancellationToken ct
        ) => Task.FromResult<FullOverlayAdmission>(new FullOverlayAdmission.Admitted([]));
    }
}
