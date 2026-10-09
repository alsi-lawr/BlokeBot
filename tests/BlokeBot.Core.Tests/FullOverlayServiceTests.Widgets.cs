using System.Net;
using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Overlays.Full;
using BlokeBot.Core.Features.Plugins;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class FullOverlayServiceTests
{
    [Test]
    public async Task Registry_RepeatedSourcesKeepIndependentConfigurationAndAudioWithoutRewritingSource()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var first = environment.Registry.Create(new("guessing"), Guid.NewGuid())!;
        var second = first with
        {
            Id = new(Guid.NewGuid()),
            Configuration = Config(new OverlayConfiguration.GuessingV1(false, 10)),
            Audio = new(true, 0.35),
        };
        var html = environment.Registry.Create(new("html"), Guid.NewGuid())! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayHtmlConfiguration(
                    "<script>window.counter = 1;</script><unknown-tag onclick='counter++'>kept</unknown-tag>",
                    "@layer arbitrary { --unrecognized: 7; }"
                )
            ),
        };
        var unresolved = first with
        {
            Id = new(Guid.NewGuid()),
            Kind = new("uninstalled:extension"),
            Configuration = JsonSerializer.SerializeToElement(new { retain = "all" }),
        };
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Sources", Document() with { Widgets = [first, second, html, unresolved] }),
                _ct
            )
        );
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            created.Overlay.Id,
            null,
            null,
            created.Overlay.Draft,
            FullOverlayDataMode.Sample,
            []
        );
        var outputs = await environment.Registry.ProjectAsync(context, _ct);
        Guess(outputs[0]).GuessCount.ShouldBe(42);
        Guess(outputs[1]).GuessCount.ShouldBeNull();
        outputs[0].Audio.ShouldBe(first.Audio);
        outputs[1].Audio.ShouldBe(second.Audio);
        outputs[2]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Html>()
            .Content.ShouldContain("<script>");
        outputs[3]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>()
            .Diagnostic.WidgetId.ShouldBe(unresolved.Id);
        await using (var db = fixture.Database.CreateDbContext())
        {
            _ = await db
                .Hosts.Where(host => host.Id == fixture.HostId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(host => host.EnabledFeatures, HostFeatureFlags.Overlays)
                );
        }
        var disabled = await environment.Registry.ProjectAsync(context, _ct);
        _ = disabled[0].Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        _ = disabled[1].Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        _ = disabled[2].Output.ShouldBeOfType<FullOverlayWidgetOutput.Html>();
        var reloaded = Value(
            await fixture.Service.GetAsync(fixture.Owner, created.Overlay.Id, _ct)
        );
        reloaded.Draft.Html.ShouldBe(created.Overlay.Draft.Html);
        reloaded.Draft.Css.ShouldBe(created.Overlay.Draft.Css);
        reloaded.Draft.Widgets[3].Configuration.GetProperty("retain").GetString().ShouldBe("all");
    }

    [Test]
    public async Task FullEventFeed_RepeatedWidgetsShareDurableDedupeAndPreviewDoesNotAdvance()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        var feed = environment.Registry.Create(new("event-feed"), Guid.NewGuid())!;
        FullOverlayEventFeeds.TryConfiguration(feed, out var feedConfiguration).ShouldBeTrue();
        var copy = feed with
        {
            Id = new(Guid.NewGuid()),
            Audio = new(true, 0.2),
            Configuration = JsonSerializer.SerializeToElement(
                new
                {
                    bindingId = feedConfiguration.BindingId,
                    feed = Config(
                        new OverlayConfiguration.EventFeedV1(
                            feedConfiguration.Feed.Capacity,
                            feedConfiguration.Feed.OverflowPolicy,
                            feedConfiguration.Feed.Kinds,
                            new OverlayAppearance(10, 20, 700, 300, ".card{opacity:.7}")
                        )
                    ),
                }
            ),
        };
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Feed", Document() with { Widgets = [feed, copy] }),
                _ct
            )
        );
        var live = Value(
            await fixture.Service.SaveAndPublishAsync(
                fixture.Owner,
                new(created.Overlay.Id, created.Overlay.Revision, "Feed", created.Overlay.Draft),
                _ct
            )
        ).Overlay;
        await environment.PointAsync("first");
        await environment.PointAsync("second");
        await environment.PointAsync("first");
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            live.Id,
            live.PublishedVersion,
            live.Revision,
            live.Draft,
            FullOverlayDataMode.Live,
            []
        );
        var first = await environment.Registry.ProjectAsync(context, _ct);
        Feed(first[0]).ShouldBeEquivalentTo(Feed(first[1]));
        first[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Source>()
            .Projection.ShouldBeOfType<OverlaySnapshotProjection.EventFeedV1>()
            .Snapshot.Appearance.ShouldNotBe(
                first[1]
                    .Output.ShouldBeOfType<FullOverlayWidgetOutput.Source>()
                    .Projection.ShouldBeOfType<OverlaySnapshotProjection.EventFeedV1>()
                    .Snapshot.Appearance
            );
        await using (var db = fixture.Database.CreateDbContext())
        {
            (await db.FullOverlayEventFeedBindings.CountAsync()).ShouldBe(1);
            (await db.OverlayEventFeedItems.CountAsync()).ShouldBe(2);
        }
        var active = Feed(first[0]).Active!;
        environment.Clock.Advance(TimeSpan.FromSeconds(40));
        var preview = await environment.Registry.ProjectAsync(context with { Version = null }, _ct);
        Feed(preview[0]).Active!.Id.ShouldBe(active.Id);
        Feed(preview[0]).Active!.DisplayDeadlineUtc.ShouldBe(active.DisplayDeadlineUtc);
        _ = await environment.Registry.ProjectAsync(
            context with
            {
                Version = null,
                DataMode = FullOverlayDataMode.Sample,
            },
            _ct
        );
        await using (var db = fixture.Database.CreateDbContext())
        {
            (
                await db.OverlayEventFeedItems.SingleAsync(item => item.Id == active.Id)
            ).Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Active);
        }
        await environment.PointAsync("first");
        var advanced = await environment.Registry.ProjectAsync(context, _ct);
        Feed(advanced[0]).Active!.Id.ShouldNotBe(active.Id);
        var restarted = new OverlayEventFeedService(
            fixture.Database,
            environment.Clock,
            environment.Services,
            NullLogger<OverlayEventFeedService>.Instance
        );
        await restarted.PresentAsync(
            new OverlayEventPresentation.PointAward
            {
                HostId = fixture.HostId,
                SourceKey = "first",
                Recipient = "viewer",
                Amount = "5",
                PointLabel = "points",
            },
            _ct
        );
        await using (var db = fixture.Database.CreateDbContext())
        {
            (await db.OverlayEventFeedItems.CountAsync()).ShouldBe(2);
        }
        await restarted.DisposeAsync();
        var archived = Value(
            await fixture.Service.ArchiveAsync(fixture.Owner, new(live.Id, live.Revision), _ct)
        );
        await environment.PointAsync("during-archive");
        _ = (await environment.Registry.ProjectAsync(context, _ct))[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        await using (var db = fixture.Database.CreateDbContext())
        {
            (
                await db.OverlayEventFeedItems.CountAsync(item =>
                    item.Lifecycle == OverlayEventFeedLifecycle.Active
                    || item.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
            ).ShouldBe(0);
        }
        _ = Value(
            await fixture.Service.RestoreAsync(
                fixture.Owner,
                new(archived.Id, archived.Revision),
                _ct
            )
        );
        await environment.PointAsync("after-restore");
        _ = (await environment.Registry.ProjectAsync(context, _ct))[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Source>();
    }

    [Test]
    public async Task MediaWidgets_UseHostOwnedCurrentUploadsAndOrdinaryReferenceLifecycle()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var environment = new WidgetEnvironment(fixture);
        await using var bytes = new MemoryStream([1, 2, 3]);
        var uploaded = (
            await environment.Media.UploadAssetAsync(
                fixture.Owner,
                "Image",
                "image/png",
                bytes,
                _ct
            )
        )
            .ShouldBeOfType<OverlayCueResult<OverlayMediaAssetView>.Succeeded>()
            .Value;
        var image = environment.Registry.Create(new("image"), Guid.NewGuid())! with
        {
            Configuration = JsonSerializer.SerializeToElement(
                new FullOverlayMediaConfiguration(uploaded.Id, false)
            ),
        };
        var created = Value(
            await fixture.Service.CreateAsync(
                fixture.Owner,
                new("Image", Document() with { Widgets = [image] }),
                _ct
            )
        );
        var context = new FullOverlayRenderContext(
            fixture.HostId,
            created.Overlay.Id,
            null,
            null,
            created.Overlay.Draft,
            FullOverlayDataMode.Live,
            []
        );
        (await environment.Registry.ProjectAsync(context, _ct))[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Media>()
            .ContentRevision.ShouldBe(1);
        _ = (
            await environment.Registry.ProjectAsync(
                context with
                {
                    HostId = fixture.OtherHostId,
                },
                _ct
            )
        )[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
        _ = (
            await environment.Media.DeleteAssetAsync(
                fixture.Owner,
                uploaded.Id,
                uploaded.ContentRevision,
                _ct
            )
        )
            .ShouldBeOfType<OverlayCueResult<Guid>.Rejected>()
            .Reason.ShouldBeOfType<OverlayCueRejection.InUse>();
        _ = Value(
            await fixture.Service.SaveAsync(
                fixture.Owner,
                new(
                    created.Overlay.Id,
                    created.Overlay.Revision,
                    "Image",
                    created.Overlay.Draft with
                    {
                        Widgets = [],
                    }
                ),
                _ct
            )
        );
        _ = (
            await environment.Media.DeleteAssetAsync(
                fixture.Owner,
                uploaded.Id,
                uploaded.ContentRevision,
                _ct
            )
        ).ShouldBeOfType<OverlayCueResult<Guid>.Succeeded>();
        _ = (await environment.Registry.ProjectAsync(context, _ct))[0]
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Unavailable>();
    }

    private static GuessingV1OverlayPresentationState.Open Guess(
        FullOverlayWidgetProjection projection
    ) =>
        projection
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Source>()
            .Projection.ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
            .Snapshot.State.ShouldBeOfType<GuessingV1OverlayPresentationState.Open>();

    private static EventFeedStatePresentation Feed(FullOverlayWidgetProjection projection) =>
        projection
            .Output.ShouldBeOfType<FullOverlayWidgetOutput.Source>()
            .Projection.ShouldBeOfType<OverlaySnapshotProjection.EventFeedV1>()
            .Snapshot.State;

    private static JsonElement Config(OverlayConfiguration config)
    {
        using var document = JsonDocument.Parse(config.ToPersistenceJson());
        return document.RootElement.Clone();
    }

    private sealed class WidgetClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class PublicDns : IOverlayDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("203.0.113.1")]);
    }

    private sealed class WidgetEnvironment : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(
            Directory.GetCurrentDirectory(),
            ".agent-workspace/overlay-composition-20261001/writer287/widgets",
            Guid.NewGuid().ToString("N")
        );
        private readonly Fixture _fixture;
        internal WidgetClock Clock { get; } = new();
        internal ServiceProvider Services { get; }
        internal OverlayEventFeedService Events { get; }
        internal EventBus<FullOverlayEventFeedIdentity> FeedChanges { get; } =
            TestEventBus.Create<FullOverlayEventFeedIdentity>();
        internal OverlayCueService Media { get; }
        internal FullOverlayWidgetRegistry Registry { get; }
        internal OverlayStateProvider Sources { get; }
        internal OverlayRemoteUrlPolicy Urls { get; }
        internal OverlayMediaMaintenanceService Maintenance { get; }
        internal string MediaRoot => OverlayMediaDirectory.DocumentDirectory(_root);

        internal WidgetEnvironment(
            Fixture fixture,
            WidgetPluginRig? plugin = null,
            IOverlayDnsResolver? dns = null,
            ILogger<FullOverlayWidgetRegistry>? logger = null,
            BlokeBotOverlayMediaOptions? mediaOptions = null,
            IOverlayMediaFileDeletion? fileDeletion = null
        )
        {
            _fixture = fixture;
            var options = Options.Create(
                new BlokeBotOptions
                {
                    StateDirectory = _root,
                    Overlays = new() { Media = mediaOptions ?? new() },
                }
            );
            _ = Directory.CreateDirectory(_root);
            Services = new ServiceCollection()
                .AddSingleton(fixture.Events)
                .AddSingleton(FeedChanges)
                .BuildServiceProvider();
            Events = new(
                fixture.Database,
                Clock,
                Services,
                NullLogger<OverlayEventFeedService>.Instance
            );
            var urls = new OverlayRemoteUrlPolicy(dns ?? new PublicDns(), options);
            Urls = urls;
            var deletion = fileDeletion ?? new SystemOverlayMediaFileDeletion();
            Maintenance = new(
                fixture.Database,
                options,
                deletion,
                Clock,
                NullLogger<OverlayMediaMaintenanceService>.Instance
            );
            Media = new(
                fixture.Database,
                new(fixture.Database, fixture.Moderator),
                urls,
                options,
                fixture.Events,
                Clock,
                deletion,
                Maintenance
            );
            var declarations = plugin?.Declarations ?? new PluginFeatureDeclarationRegistry();
            var features = plugin?.Features ?? new PluginFeatureSnapshotRegistry();
            var runtime = plugin?.Runtime ?? new PluginRuntimeSnapshotRegistry();
            var plugins = new PluginWidgetCatalog(declarations, features, runtime);
            var assets = new PluginWidgetAssetService(
                plugins,
                new(
                    (IPluginPackageAssetResolver?)plugin?.Package
                        ?? new UnavailablePluginPackageAssetResolver()
                )
            );
            Sources = new(
                fixture.Database,
                new(),
                Clock,
                Events,
                new BlokeBot.Core.Features.PlayWithViewers.PlayQueueService(
                    fixture.Database,
                    fixture.Events,
                    Clock
                )
            );
            Registry = new(
                Sources,
                Events,
                Media,
                urls,
                fixture.Database,
                plugins,
                new PluginDispatchInvoker(new(features, runtime), runtime, new(), Clock),
                assets,
                logger ?? NullLogger<FullOverlayWidgetRegistry>.Instance
            );
        }

        internal Task PointAsync(string key) =>
            Events.PresentAsync(
                new OverlayEventPresentation.PointAward
                {
                    HostId = _fixture.HostId,
                    SourceKey = key,
                    Recipient = "viewer",
                    Amount = "5",
                    PointLabel = "points",
                },
                _ct
            );

        public async ValueTask DisposeAsync()
        {
            await Events.DisposeAsync();
            Maintenance.Dispose();
            await Services.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }
}
