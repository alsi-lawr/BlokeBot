using System.Text.Json.Nodes;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Hosting;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class EventFeedOverlayTests
{
    [Test]
    public void EventFeedV1_IsStrictBoundedAndEscapesSafePlaceholders()
    {
        var configuration = OverlayConfiguration.EventFeedV1.Default;
        var persisted = configuration.ToPersistenceJson();
        OverlayConfiguration
            .Parse(OverlayType.EventFeed, persisted)
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>();
        OverlayConfiguration
            .Parse(
                OverlayType.EventFeed,
                persisted.Replace("\"capacity\":10", "\"capacity\":26", StringComparison.Ordinal)
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        foreach (
            var malformed in new[]
            {
                WithNull(persisted, "schemaVersion"),
                WithNull(persisted, "capacity"),
                WithNull(persisted, "overflowPolicy"),
                WithNull(persisted, "kinds"),
                WithNull(persisted, "kinds", "pointAward"),
                WithNull(persisted, "kinds", "pointAward", "enabled"),
                WithNull(persisted, "kinds", "pointAward", "template"),
                WithNull(persisted, "kinds", "pointAward", "priority"),
                WithNull(persisted, "kinds", "pointAward", "durationSeconds"),
            }
        )
        {
            OverlayConfiguration
                .Parse(OverlayType.EventFeed, malformed)
                .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        }
        OverlayConfiguration
            .Parse(OverlayType.EventFeed, persisted[..^1] + ",\"extra\":true}")
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        OverlayConfiguration
            .Parse(
                OverlayType.EventFeed,
                persisted.Replace(
                    "\"overflowPolicy\":\"dropNewest\"",
                    "\"overflowPolicy\":\"0\"",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        OverlayConfiguration
            .Parse(
                OverlayType.EventFeed,
                persisted.Replace(
                    "\"priority\":\"normal\"",
                    "\"priority\":\"0\"",
                    StringComparison.Ordinal
                )
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();

        var rendered = EventFeedTemplateRenderer.Render(
            configuration.Kinds[OverlayEventFeedKind.PointAward],
            new OverlayEventPresentation.PointAward
            {
                HostId = 1,
                SourceKey = "ledger-1",
                Recipient = "<script>",
                Amount = "5",
                PointLabel = "points & cheers",
            }
        );
        rendered.ShouldBe("&lt;script&gt; received 5 points &amp; cheers");
        EventFeedTemplateRenderer
            .Render(
                configuration.Kinds[OverlayEventFeedKind.PointAward],
                new OverlayEventPresentation.PointAward
                {
                    HostId = 1,
                    SourceKey = "ledger-braces",
                    Recipient = "{viewer}",
                    Amount = "5",
                    PointLabel = "points",
                }
            )
            .ShouldBe("{viewer} received 5 points");
        Should.Throw<ArgumentException>(() =>
            EventFeedTemplateRenderer.Render(
                new EventFeedKindConfiguration(true, "{actor}", OverlayEventFeedPriority.Normal, 5),
                new OverlayEventPresentation.PointAward
                {
                    HostId = 1,
                    SourceKey = "ledger-2",
                    Recipient = "viewer",
                    Amount = "5",
                    PointLabel = "points",
                }
            )
        );
    }

    [Test]
    public void Projection_DecodesDurableTextExactlyOnceAndRendererKeepsMarkupInert()
    {
        EventFeedProjectionText.DecodeOnce("&lt;b&gt;A &amp; B&lt;/b&gt;").ShouldBe("<b>A & B</b>");
        EventFeedProjectionText.DecodeOnce("&#60;tag&#62;").ShouldBe("<tag>");
        EventFeedProjectionText
            .DecodeOnce("&amp;lt;already escaped&amp;gt;")
            .ShouldBe("&lt;already escaped&gt;");
        EventFeedProjectionText.DecodeOnce("ordinary &amp; text").ShouldBe("ordinary & text");

        OverlayBrowserSourceAssets.JavaScript.ShouldContain("element.textContent = text");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("host.append(body)");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("body.textContent = text");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("innerHTML");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("outerHTML");
    }

    [Test]
    public async Task Admission_IsHostIsolatedDeduplicatedBoundedAndUsesConfiguredOverflow()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 2,
            EventFeedOverflowPolicy.ReplaceNewestSameKind
        );
        var otherOverlayId = await fixture.AddOtherHostOverlayAsync();
        await fixture.PresentPointAsync("ledger-1", "one");
        await fixture.PresentPointAsync("ledger-2", "two");
        await fixture.PresentPointAsync("ledger-3", "three");
        await fixture.PresentPointAsync("ledger-3", "duplicate");

        await using var db = await fixture.Database.CreateDbContextAsync();
        var items = await db.OverlayEventFeedItems.OrderBy(x => x.Id).ToListAsync();
        items.Count.ShouldBe(3);
        items.Count(x => x.SourceKey == "ledger-3").ShouldBe(1);
        items
            .Single(x => x.SourceKey == "ledger-2")
            .Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Suppressed);
        items
            .Single(x => x.SourceKey == "ledger-1")
            .Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Active);
        items
            .Single(x => x.SourceKey == "ledger-3")
            .Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Queued);
        items.ShouldAllBe(x => x.HostId == fixture.HostId);
        items.ShouldAllBe(x => x.OverlayInstanceId != otherOverlayId);
    }

    [Test]
    public async Task TimeProviderAdvancement_IsDurableAndReconnectDoesNotReplayConsumedCard()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        await fixture.PresentPointAsync("ledger-1", "one");
        await fixture.PresentPointAsync("ledger-2", "two");
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));

        var state = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        state!.Active!.Body.ShouldContain("two");
        state.Pending.ShouldBeEmpty();
        var reconnect = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        reconnect!.Active!.Id.ShouldBe(state.Active.Id);
        await using var db = await fixture.Database.CreateDbContextAsync();
        db.OverlayEventFeedItems.Single(x => x.SourceKey == "ledger-1")
            .Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Consumed);
    }

    [Test]
    public async Task QueuedCard_KeepsAdmissionDurationAfterConfigurationChanges()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        await fixture.PresentPointAsync("ledger-1", "one");
        await fixture.PresentPointAsync("ledger-2", "two");
        await fixture.ChangePointDurationAsync(30);
        fixture.Clock.Advance(TimeSpan.FromSeconds(7));

        var promoted = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);

        promoted!.Active!.Body.ShouldContain("two");
        promoted.Active.DisplayDeadlineUtc.ShouldBe(fixture.Clock.GetUtcNow().AddSeconds(6));
        await using var db = await fixture.Database.CreateDbContextAsync();
        db.OverlayEventFeedItems.Single(x => x.SourceKey == "ledger-2").DurationSeconds.ShouldBe(6);
    }

    [Test]
    public async Task RestartRead_SuppressesPersistedDisabledSourceAndNeverReplaysIt()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        await fixture.PresentPointAsync("ledger-1", "one");
        await fixture.PresentGuessAsync("guess-1");
        await fixture.SetFeaturesAsync(HostFeatureFlags.Overlays | HostFeatureFlags.Guessing);

        var recovered = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);

        recovered!.Active!.Kind.ShouldBe("guessingWinner");
        recovered.Pending.ShouldBeEmpty();
        await fixture.SetFeaturesAsync(HostFeatureFlags.All);
        var restored = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        restored!.Active!.Kind.ShouldBe("guessingWinner");
        await using var db = await fixture.Database.CreateDbContextAsync();
        db.OverlayEventFeedItems.Single(x => x.SourceKey == "ledger-1")
            .Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Suppressed);
    }

    [Test]
    public async Task LongUnicodeMarkupEvent_PersistsEscapedAndProjectsAsOneDecodedCard()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        var recipient = string.Concat(Enumerable.Repeat("👩🏽‍💻<script>&value;長", 40));
        await fixture.PresentPointAsync("ledger-long", recipient);

        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            var durable = await db.OverlayEventFeedItems.SingleAsync();
            durable.Body.Length.ShouldBeGreaterThan(500);
            durable.Body.ShouldContain("&lt;script&gt;");
            durable.Body.ShouldContain("&amp;value;");
        }
        var state = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        state!.Active!.Body.ShouldStartWith(recipient);
        state.Pending.ShouldBeEmpty();
        state.Active.Id.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task RepresentativeSample_UsesTheSameSingleDecodeProjectionBoundary()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        var kinds = OverlayConfiguration.EventFeedV1.Default.Kinds.ToDictionary(
            pair => pair.Key,
            pair =>
                pair.Key == OverlayEventFeedKind.PointAward
                    ? new EventFeedKindConfiguration(
                        pair.Value.Enabled,
                        "<strong>{recipient}</strong> & {pointLabel}",
                        pair.Value.Priority,
                        pair.Value.DurationSeconds
                    )
                    : pair.Value
        );
        var configuration = new OverlayConfiguration.EventFeedV1(
            10,
            EventFeedOverflowPolicy.DropNewest,
            kinds
        );
        var instance = new ResolvedOverlayInstance(
            fixture.HostId,
            fixture.Instance.OverlayId,
            OverlayType.EventFeed,
            configuration,
            fixture.Instance.Revision
        );
        IOverlayStateProvider provider = new OverlayStateProvider(
            fixture.Database,
            new OverlayServerEpoch(),
            fixture.Clock,
            fixture.Service
        );

        var sample = (
            await provider.ProjectEventFeedSampleAsync(
                instance,
                OverlayEventFeedKind.PointAward,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlaySnapshotProjection.EventFeedV1>();

        sample.Snapshot.State.Active!.Body.ShouldBe("<strong>nightowl</strong> & points");
    }

    [Test]
    public async Task DisabledParentOrSource_AdmitsNothingAndSuppressedCardsNeverReplay()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        await fixture.PresentPointAsync("ledger-1", "one");
        await fixture.SetFeaturesAsync(HostFeatureFlags.Overlays);
        await fixture.Service.SuppressSourceAsync(
            fixture.HostId,
            HostFeatureFlags.Points,
            CancellationToken.None
        );
        await fixture.PresentPointAsync("ledger-2", "two");
        await fixture.SetFeaturesAsync(HostFeatureFlags.All);

        var state = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        state!.Active.ShouldBeNull();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var items = await db.OverlayEventFeedItems.ToListAsync();
        items.ShouldHaveSingleItem().Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Suppressed);
    }

    [Test]
    public async Task DisabledOverlayParent_SuppressesPersistedCardsAndRejectsNewAdmissions()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        await fixture.PresentPointAsync("ledger-1", "one");
        await fixture.SetFeaturesAsync(HostFeatureFlags.Points | HostFeatureFlags.Guessing);

        (await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None)).ShouldBeNull();
        await fixture.PresentPointAsync("ledger-2", "two");
        await fixture.SetFeaturesAsync(HostFeatureFlags.All);
        var restored = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        restored!.Active.ShouldBeNull();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var items = await db.OverlayEventFeedItems.ToListAsync();
        items.ShouldHaveSingleItem().Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Suppressed);
    }

    [Test]
    public async Task ProductionFeatureToggle_UsesRegisteredObserverAndNeverReplaysSourceRows()
    {
        await using var fixture = await Fixture.CreateAsync(
            capacity: 10,
            EventFeedOverflowPolicy.DropNewest
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(fixture.Database);
        services.AddSingleton<TimeProvider>(fixture.Clock);
        services.AddSingleton(TestEventBus.Create<AppEventKind>());
        services.AddSingleton<HostedChannelChangeNotifier>();
        services.AddSingleton<HostFeatureService>();
        services.AddBlokeBotOverlays();
        await using var provider = services.BuildServiceProvider();
        var feed = provider.GetRequiredService<OverlayEventFeedService>();
        provider
            .GetServices<IHostFeatureChangeObserver>()
            .ShouldHaveSingleItem()
            .ShouldBeSameAs(feed);
        var presenter = provider.GetRequiredService<IOverlayEventPresenter>();
        var features = provider.GetRequiredService<HostFeatureService>();
        await presenter.PresentAsync(Point("point-active", "one"), CancellationToken.None);
        await presenter.PresentAsync(Point("point-queued", "two"), CancellationToken.None);
        await presenter.PresentAsync(Guess("guess-first"), CancellationToken.None);

        await features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Points,
            CancellationToken.None
        );

        await using (var disabled = await fixture.Database.CreateDbContextAsync())
        {
            var host = await disabled.Hosts.SingleAsync(x => x.Id == fixture.HostId);
            host.EnabledFeatures.Contains(HostFeatureFlags.Points).ShouldBeFalse();
            var rows = await disabled.OverlayEventFeedItems.ToListAsync();
            rows.Where(x =>
                    x.Kind is OverlayEventFeedKind.PointAward or OverlayEventFeedKind.GiveawayWinner
                )
                .ShouldAllBe(x => x.Lifecycle == OverlayEventFeedLifecycle.Suppressed);
            rows.Single(x => x.SourceKey == "guess-first")
                .Lifecycle.ShouldBe(OverlayEventFeedLifecycle.Queued);
        }
        await features.EnableAsync(fixture.HostId, HostFeatureFlags.Points, CancellationToken.None);
        var guessingActive = await feed.ReadAsync(fixture.Instance, CancellationToken.None);
        guessingActive!.Active!.Kind.ShouldBe("guessingWinner");
        await presenter.PresentAsync(Guess("guess-second"), CancellationToken.None);

        await features.DisableAsync(
            fixture.HostId,
            HostFeatureFlags.Guessing,
            CancellationToken.None
        );

        await using (var disabled = await fixture.Database.CreateDbContextAsync())
        {
            var host = await disabled.Hosts.SingleAsync(x => x.Id == fixture.HostId);
            host.EnabledFeatures.Contains(HostFeatureFlags.Guessing).ShouldBeFalse();
            var guessing = await disabled
                .OverlayEventFeedItems.Where(x => x.Kind == OverlayEventFeedKind.GuessingWinner)
                .ToListAsync();
            guessing.Count.ShouldBe(2);
            guessing.ShouldAllBe(x => x.Lifecycle == OverlayEventFeedLifecycle.Suppressed);
        }
        await features.EnableAsync(
            fixture.HostId,
            HostFeatureFlags.Guessing,
            CancellationToken.None
        );
        var restored = await feed.ReadAsync(fixture.Instance, CancellationToken.None);
        restored!.Active.ShouldBeNull();
        restored.Pending.ShouldBeEmpty();

        OverlayEventPresentation.PointAward Point(string sourceKey, string recipient) =>
            new()
            {
                HostId = fixture.HostId,
                SourceKey = sourceKey,
                Recipient = recipient,
                Amount = "5",
                PointLabel = "points",
            };

        OverlayEventPresentation.GuessingWinner Guess(string sourceKey) =>
            new()
            {
                HostId = fixture.HostId,
                SourceKey = sourceKey,
                RoundName = "Final",
                WinningAnswer = "Blue",
                Winners = ["winner"],
                Amount = "0",
                PointLabel = "points",
            };
    }

    [Test]
    public async Task Scheduler_ServesAtMostThreeHighCardsBeforeWaitingNormalCard()
    {
        await using var fixture = await Fixture.CreateAsync(10, EventFeedOverflowPolicy.DropNewest);
        await fixture.PresentGuessAsync("h1");
        await fixture.PresentGuessAsync("h2");
        await fixture.PresentGuessAsync("h3");
        await fixture.PresentGuessAsync("h4");
        await fixture.PresentPointAsync("normal", "normal-viewer");

        for (var index = 0; index < 3; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(9));
            await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        }
        var state = await fixture.Service.ReadAsync(fixture.Instance, CancellationToken.None);
        state!.Active!.Kind.ShouldBe("pointAward");
        state.Pending.ShouldContain(card => card.Kind == "guessingWinner");
    }

    [Test]
    public async Task LiveTransport_PublishesDecodedBaselineStateAndSampleEvents()
    {
        var instance = new ResolvedOverlayInstance(
            71,
            Guid.NewGuid(),
            OverlayType.EventFeed,
            OverlayConfiguration.EventFeedV1.Default,
            new OverlayRevision(3)
        );
        var provider = new FixedEventFeedProvider(instance);
        await using var coordinator = new OverlayLiveCoordinator(
            new OverlayServerEpoch(),
            provider,
            TimeProvider.System,
            TestEventBus.Create<AppEventKind>(),
            NullLogger<OverlayLiveCoordinator>.Instance
        );
        await coordinator.StartAsync(CancellationToken.None);
        var opened = await coordinator.OpenAsync(
            instance,
            coordinator.Generation,
            CancellationToken.None
        );
        var connection = opened.ShouldBeOfType<OverlayLiveOpenResult.Opened>().Connection;

        var baseline = (
            await ReadLiveAsync(connection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.EventFeedBaseline>();
        baseline.Envelope.EventType.ShouldBe("baseline");
        baseline.Envelope.Payload.Animation.ShouldBe("none");
        baseline.Envelope.Payload.State.Active!.Body.ShouldBe("<b>decoded</b>");

        coordinator.PublishState(instance);
        var state = (
            await ReadLiveAsync(connection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.EventFeedEvent>();
        state.Envelope.EventType.ShouldBe("state");
        state.Envelope.Payload.Animation.ShouldBe("card");
        coordinator.PublishTest(instance);
        var sample = (
            await ReadLiveAsync(connection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.EventFeedEvent>();
        sample.Envelope.EventType.ShouldBe("test");
        sample.Envelope.Payload.Animation.ShouldBe("sample");
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public void BrowserDashboardAndHelp_ExposeBoundedSamplesAndUnicodeSafeUpwardWrapping()
    {
        var javascript = OverlayBrowserSourceAssets.JavaScript;
        javascript.ShouldContain("svgElement(\"foreignObject\"");
        javascript.ShouldContain("document.createElement(\"div\")");
        javascript.ShouldContain("body.textContent = text");
        javascript.ShouldContain("eventFeedBody.body.scrollHeight");
        javascript.ShouldContain("const scaleY = appearance.height / naturalHeight");
        javascript.ShouldNotContain("maximumCardHeight");
        javascript.ShouldContain("translate(${appearance.x} ${appearance.y + appearance.height})");
        javascript.ShouldContain("\"data-source-card-id\": String(card.id)");
        javascript.ShouldNotContain("Intl.Segmenter");
        javascript.ShouldNotContain("eventFeedGraphemes");
        javascript.ShouldNotContain("wrapEventFeedBody");
        javascript.ShouldNotContain("\"tspan\"");
        javascript.ShouldNotContain("substring(");
        var stylesheet = OverlayBrowserSourceAssets.Stylesheet;
        stylesheet.ShouldContain("white-space: pre-wrap");
        stylesheet.ShouldContain("overflow-wrap: anywhere");
        stylesheet.ShouldContain("font-size: 32px");
        stylesheet.ShouldContain("line-height: 44px");

        var dashboard = File.ReadAllText(SourcePath("OverlaysPage.razor"));
        dashboard.ShouldContain("Event feed sample event");
        dashboard.ShouldContain("Point award");
        dashboard.ShouldContain("Guessing winner");
        dashboard.ShouldContain("Giveaway winner");
        dashboard.ShouldContain("When full");
        dashboard.ShouldContain("Maximum waiting cards");
        var help = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    "BlokeBot.Core",
                    "Components",
                    "Layout",
                    "PageHelpButton.razor.cs"
                )
            )
        );
        help.ShouldContain("Event Feed");
        help.ShouldContain("Saved settings remain");
    }

    private static async Task<OverlayLiveTransportMessage> ReadLiveAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await connection.Messages.ReadAsync(timeout.Token);
    }

    private static string WithNull(string json, params string[] path)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var current = root;
        foreach (var segment in path[..^1])
        {
            current = current[segment]!.AsObject();
        }
        current[path[^1]] = null;
        return root.ToJsonString();
    }

    private static string SourcePath(string fileName) =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "BlokeBot.Core",
                "Features",
                "Overlays",
                fileName
            )
        );

    private sealed class FixedEventFeedProvider(ResolvedOverlayInstance instance)
        : IOverlayStateProvider
    {
        public Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance requested,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            requested.ShouldBe(instance);
            return Task.FromResult<OverlaySnapshotProjection>(
                new OverlaySnapshotProjection.EventFeedV1(
                    new EventFeedV1OverlaySnapshot
                    {
                        ServerEpoch = Guid.NewGuid(),
                        Sequence = instance.Revision.Value,
                        GeneratedAtUtc = DateTimeOffset.UnixEpoch,
                        Animation = "none",
                        State = new EventFeedStatePresentation(
                            new EventFeedCardPresentation(
                                11,
                                "pointAward",
                                "normal",
                                "Points awarded",
                                "<b>decoded</b>",
                                DateTimeOffset.UnixEpoch,
                                DateTimeOffset.UnixEpoch.AddSeconds(6)
                            ),
                            []
                        ),
                    }
                )
            );
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SqliteBlokeBotDbFactory database,
            int hostId,
            ManualTimeProvider clock,
            OverlayEventFeedService service,
            ResolvedOverlayInstance instance
        )
        {
            Database = database;
            HostId = hostId;
            Clock = clock;
            Service = service;
            Instance = instance;
        }

        internal SqliteBlokeBotDbFactory Database { get; }
        internal int HostId { get; }
        internal ManualTimeProvider Clock { get; }
        internal OverlayEventFeedService Service { get; }
        internal ResolvedOverlayInstance Instance { get; }

        internal static async Task<Fixture> CreateAsync(
            int capacity,
            EventFeedOverflowPolicy overflow
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var configuration = new OverlayConfiguration.EventFeedV1(
                capacity,
                overflow,
                OverlayConfiguration.EventFeedV1.Default.Kinds
            );
            int hostId;
            var publicId = Guid.NewGuid();
            await using (var db = await database.CreateDbContextAsync())
            {
                var host = new BotHost
                {
                    TwitchUserId = Guid.NewGuid().ToString(),
                    Login = "host",
                    DisplayName = "Host",
                    EnabledFeatures = HostFeatureFlags.All,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                db.Hosts.Add(host);
                await db.SaveChangesAsync();
                hostId = host.Id;
                db.OverlayInstances.Add(
                    new OverlayInstance
                    {
                        PublicId = publicId,
                        HostId = hostId,
                        Name = "Feed",
                        Type = OverlayType.EventFeed,
                        IsEnabled = true,
                        ConfigurationJson = configuration.ToPersistenceJson(),
                        AccessKeyDigest = new byte[32],
                        KeyVersion = 1,
                        Revision = 1,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                    }
                );
                await db.SaveChangesAsync();
            }
            var clock = new ManualTimeProvider(
                new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)
            );
            var service = new OverlayEventFeedService(
                database,
                clock,
                new PublisherServices(),
                NullLogger<OverlayEventFeedService>.Instance
            );
            return new Fixture(
                database,
                hostId,
                clock,
                service,
                new ResolvedOverlayInstance(
                    hostId,
                    publicId,
                    OverlayType.EventFeed,
                    configuration,
                    new OverlayRevision(1)
                )
            );
        }

        internal Task PresentPointAsync(string sourceKey, string recipient) =>
            Service.PresentAsync(
                new OverlayEventPresentation.PointAward
                {
                    HostId = HostId,
                    SourceKey = sourceKey,
                    Recipient = recipient,
                    Amount = "5",
                    PointLabel = "points",
                },
                CancellationToken.None
            );

        internal Task PresentGuessAsync(string sourceKey) =>
            Service.PresentAsync(
                new OverlayEventPresentation.GuessingWinner
                {
                    HostId = HostId,
                    SourceKey = sourceKey,
                    RoundName = "Final",
                    WinningAnswer = "Blue",
                    Winners = ["winner"],
                    Amount = "10",
                    PointLabel = "points",
                },
                CancellationToken.None
            );

        internal async Task SetFeaturesAsync(HostFeatureFlags flags)
        {
            await using var db = await Database.CreateDbContextAsync();
            await db
                .Hosts.Where(x => x.Id == HostId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EnabledFeatures, flags));
        }

        internal async Task<long> AddOtherHostOverlayAsync()
        {
            await using var db = await Database.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = Guid.NewGuid().ToString(),
                Login = "other-host",
                DisplayName = "Other host",
                EnabledFeatures = HostFeatureFlags.All,
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
            var overlay = new OverlayInstance
            {
                PublicId = Guid.NewGuid(),
                HostId = host.Id,
                Name = "Other feed",
                Type = OverlayType.EventFeed,
                IsEnabled = true,
                ConfigurationJson = OverlayConfiguration.EventFeedV1.Default.ToPersistenceJson(),
                AccessKeyDigest = Enumerable.Repeat((byte)1, 32).ToArray(),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = Clock.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = Clock.GetUtcNow().UtcDateTime,
            };
            db.OverlayInstances.Add(overlay);
            await db.SaveChangesAsync();
            return overlay.Id;
        }

        internal async Task ChangePointDurationAsync(int durationSeconds)
        {
            var kinds = OverlayConfiguration.EventFeedV1.Default.Kinds.ToDictionary(
                pair => pair.Key,
                pair =>
                    pair.Key == OverlayEventFeedKind.PointAward
                        ? new EventFeedKindConfiguration(
                            pair.Value.Enabled,
                            pair.Value.Template,
                            pair.Value.Priority,
                            durationSeconds
                        )
                        : pair.Value
            );
            var changed = new OverlayConfiguration.EventFeedV1(
                OverlayConfiguration.EventFeedV1.Default.Capacity,
                OverlayConfiguration.EventFeedV1.Default.OverflowPolicy,
                kinds
            );
            await using var db = await Database.CreateDbContextAsync();
            await db
                .OverlayInstances.Where(x => x.PublicId == Instance.OverlayId)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.ConfigurationJson, changed.ToPersistenceJson())
                );
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class PublisherServices : IServiceProvider
    {
        private readonly IOverlayLivePublisher _publisher = new Publisher();

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IOverlayLivePublisher) ? _publisher : null;
    }

    private sealed class Publisher : IOverlayLivePublisher
    {
        public void PublishState(ResolvedOverlayInstance instance) { }

        public void PublishTest(ResolvedOverlayInstance instance) { }
    }
}
