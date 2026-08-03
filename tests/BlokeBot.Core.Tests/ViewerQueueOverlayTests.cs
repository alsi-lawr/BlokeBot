using System.Text.Json;
using System.Text.Json.Nodes;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerQueueOverlayTests
{
    [Test]
    public void Configuration_IsStrictBoundedAndUsesTheSharedAppearance()
    {
        var appearance = new OverlayAppearance(25, 40, 1200, 760, ".card{opacity:.9;}");
        var configuration = new OverlayConfiguration.ViewerQueueV1(17, 0, 12, appearance);
        var persisted = configuration.ToPersistenceJson();

        var parsed = OverlayConfiguration
            .Parse(OverlayType.ViewerQueue, persisted)
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.ViewerQueueV1>();

        parsed.QueueId.ShouldBe(17);
        parsed.CurrentRows.ShouldBe(0);
        parsed.NextRows.ShouldBe(12);
        parsed.Appearance.ShouldBe(appearance);
        foreach (
            var invalid in new[]
            {
                ReplaceNumber(persisted, "queueId", 0),
                ReplaceNumber(persisted, "currentRows", 13),
                ReplaceNumber(persisted, "nextRows", -1),
                WithNull(persisted, "appearance"),
                persisted[..^1] + ",\"extra\":true}",
            }
        )
        {
            _ = OverlayConfiguration
                .Parse(OverlayType.ViewerQueue, invalid)
                .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        }
    }

    [Test]
    public async Task Projection_UsesPublicOrderIndependentBoundsTrueTotalAndNameControl()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        var queue = Success(
            await service.ConfigureAsync(hostId, Queue(4, showNames: true), CancellationToken.None)
        ).Value;
        for (var index = 0; index < 4; index++)
        {
            _ = Success(
                await service.JoinAsync(
                    hostId,
                    queue.Slug,
                    Join($"current{index}", index),
                    CancellationToken.None
                )
            );
        }
        _ = Success(
            await service.SelectPartyAsync(hostId, queue.Slug, false, CancellationToken.None)
        );
        for (var index = 0; index < 5; index++)
        {
            _ = Success(
                await service.JoinAsync(
                    hostId,
                    queue.Slug,
                    Join($"waiting{index}", index + 10),
                    CancellationToken.None
                )
            );
        }
        await using (var privateUpdate = await database.CreateDbContextAsync())
        {
            var privateEntry = await privateUpdate.PlayQueueEntries.FirstAsync();
            privateEntry.PrivateModeratorNote = "PRIVATE-MODERATOR-NOTE";
            _ = await privateUpdate.SaveChangesAsync();
        }

        var state = await service.ReadOverlayStateAsync(
            hostId,
            queue.Id,
            2,
            3,
            CancellationToken.None
        );

        _ = state.ShouldNotBeNull();
        state.TotalQueueSize.ShouldBe(5);
        state.CurrentParty.Count.ShouldBe(2);
        state
            .Next.Select(value => value.DisplayName)
            .ShouldBe(["waiting0", "waiting1", "waiting2"]);
        state.Next.ShouldAllBe(value => value.Fields.Count == 3);
        state
            .Next[0]
            .Fields.ShouldBe([
                new("platform", "Platform", "PC"),
                new("region", "Region", "Region 10"),
                new("preferred-role", "Preferred role", ""),
            ]);
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        json.ShouldNotContain("PRIVATE-MODERATOR-NOTE");
        json.ShouldNotContain("twitch-waiting");
        json.ShouldNotContain("normalizedLogin");
        json.ShouldNotContain("priority");
        json.ShouldNotContain("joinedAtUtc");
        json.ShouldNotContain("entryId");

        _ = Success(
            await service.ConfigureAsync(hostId, Queue(4, showNames: false), CancellationToken.None)
        );
        var anonymous = await service.ReadOverlayStateAsync(
            hostId,
            queue.Id,
            12,
            12,
            CancellationToken.None
        );
        anonymous!
            .CurrentParty.Concat(anonymous.Next)
            .ShouldAllBe(value => value.DisplayName == null);
        (
            await service.ReadOverlayStateAsync(hostId, queue.Id, 13, 0, CancellationToken.None)
        ).ShouldBeNull();
        var otherHostId = await SeedHostAsync(database, "beta");
        (
            await service.ReadOverlayStateAsync(
                otherHostId,
                queue.Id,
                12,
                12,
                CancellationToken.None
            )
        ).ShouldBeNull();
    }

    [Test]
    public async Task ProjectionAndSamples_RequireBothParentsAndReenableWithoutAnimation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var service = CreateService(database);
        var queue = Success(
            await service.ConfigureAsync(hostId, Queue(2, true), CancellationToken.None)
        ).Value;
        _ = Success(
            await service.JoinAsync(hostId, queue.Slug, Join("retained", 1), CancellationToken.None)
        );
        var appearance = new OverlayAppearance(10, 20, 900, 700, ".card{fill:#fff;}");
        var instance = Instance(hostId, queue.Id, appearance);
        var provider = new OverlayStateProvider(
            database,
            new OverlayServerEpoch(),
            TimeProvider.System,
            playQueues: service
        );

        var enabled = (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.ViewerQueueV1>();
        enabled.Snapshot.Appearance.ShouldBe(appearance);
        enabled.Snapshot.Animation.ShouldBe("none");
        enabled.Snapshot.State.TotalQueueSize.ShouldBe(1);
        _ = (
            await service.ReadOverlayStateAsync(hostId, queue.Id, 12, 12, CancellationToken.None)
        ).ShouldNotBeNull();

        await SetFeaturesAsync(database, hostId, HostFeatureFlags.PlayWithViewers);
        _ = (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
        (
            await service.ReadOverlayStateAsync(hostId, queue.Id, 12, 12, CancellationToken.None)
        ).ShouldBeNull();
        _ = (
            await provider.ProjectViewerQueueSampleAsync(
                instance,
                ViewerQueueOverlaySampleState.PartyChanged,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();

        await SetFeaturesAsync(database, hostId, HostFeatureFlags.Overlays);
        _ = (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
        (
            await service.ReadOverlayStateAsync(hostId, queue.Id, 12, 12, CancellationToken.None)
        ).ShouldBeNull();

        await SetFeaturesAsync(database, hostId, HostFeatureFlags.All);
        var restored = (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.ViewerQueueV1>();
        restored.Snapshot.Appearance.ShouldBe(appearance);
        restored.Snapshot.Animation.ShouldBe("none");
        restored.Snapshot.State.TotalQueueSize.ShouldBe(1);
        _ = (
            await service.ReadOverlayStateAsync(hostId, queue.Id, 12, 12, CancellationToken.None)
        ).ShouldNotBeNull();
        (
            await provider.ProjectViewerQueueSampleAsync(
                instance,
                ViewerQueueOverlaySampleState.PartyChanged,
                CancellationToken.None
            )
        )
            .ShouldBeOfType<OverlaySnapshotProjection.ViewerQueueV1>()
            .Snapshot.Animation.ShouldBe("none");
    }

    [Test]
    public async Task QueueMutations_NotifyAfterCommitWithFixedTransitions()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var notifier = new PlayQueueChangeNotifier();
        var observer = new CommittedObserver(database);
        using var subscription = notifier.Subscribe(observer);
        var service = CreateService(database, notifier);
        var queue = Success(
            await service.ConfigureAsync(hostId, Queue(1, true), CancellationToken.None)
        ).Value;
        var joined = Success(
            await service.JoinAsync(hostId, queue.Slug, Join("viewer", 1), CancellationToken.None)
        );
        _ = Success(
            await service.StartReadyCheckAsync(
                hostId,
                joined.Value.InternalEntryId,
                CancellationToken.None
            )
        );
        _ = Success(
            await service.ReadyAsync(
                hostId,
                queue.Slug,
                new("viewer", "twitch-viewer", "viewer"),
                CancellationToken.None
            )
        );
        _ = Success(
            await service.SelectPartyAsync(hostId, queue.Slug, false, CancellationToken.None)
        );
        _ = Success(await service.SetOpenAsync(hostId, queue.Slug, false, CancellationToken.None));
        _ = Success(await service.SetOpenAsync(hostId, queue.Slug, true, CancellationToken.None));

        observer
            .Changes.Select(value => value.Transition)
            .ShouldBe([
                PlayQueueOverlayTransition.None,
                PlayQueueOverlayTransition.None,
                PlayQueueOverlayTransition.SelectedNext,
                PlayQueueOverlayTransition.ReadyOutcome,
                PlayQueueOverlayTransition.PartyChanged,
                PlayQueueOverlayTransition.None,
                PlayQueueOverlayTransition.None,
            ]);
        observer.Changes.ShouldAllBe(value => value.HostId == hostId && value.QueueId == queue.Id);
        observer.EventCounts.ShouldBe([1, 2, 3, 4, 5, 6, 7]);
    }

    [Test]
    public async Task IdempotentMutation_NotifiesCommittedReadinessConvergenceOnlyOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha");
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
        var notifier = new PlayQueueChangeNotifier();
        var observer = new CommittedObserver(database);
        using var subscription = notifier.Subscribe(observer);
        var service = CreateService(database, notifier, clock);
        var queue = Success(
            await service.ConfigureAsync(hostId, Queue(1, true), CancellationToken.None)
        ).Value;
        var joined = Success(
            await service.JoinAsync(hostId, queue.Slug, Join("viewer", 1), CancellationToken.None)
        );
        _ = Success(
            await service.StartReadyCheckAsync(
                hostId,
                joined.Value.InternalEntryId,
                CancellationToken.None
            )
        );
        observer.Changes.Clear();
        observer.EventCounts.Clear();

        clock.Advance(TimeSpan.FromSeconds(120));
        var converged = Success(
            await service.SetOpenAsync(hostId, queue.Slug, true, CancellationToken.None)
        );

        converged.WasIdempotent.ShouldBeTrue();
        observer
            .Changes.ShouldHaveSingleItem()
            .ShouldBe(
                new PlayQueueCommittedChange(
                    hostId,
                    queue.Id,
                    PlayQueueOverlayTransition.ReadyOutcome
                )
            );
        observer.EventCounts.ShouldBe([4]);

        var unchanged = Success(
            await service.SetOpenAsync(hostId, queue.Slug, true, CancellationToken.None)
        );

        unchanged.WasIdempotent.ShouldBeTrue();
        _ = observer.Changes.ShouldHaveSingleItem();
        observer.EventCounts.ShouldBe([4]);
    }

    [Test]
    public async Task LiveUpdates_AreHostAndQueueScopedWithNonReplayedBaselineAnimations()
    {
        var notifier = new PlayQueueChangeNotifier();
        var first = Instance(1, 10);
        var otherQueue = Instance(1, 20);
        var otherHost = Instance(2, 10);
        await using var coordinator = new OverlayLiveCoordinator(
            new OverlayServerEpoch(),
            new FixedQueueProvider(),
            TimeProvider.System,
            TestEventBus.Create<AppEventKind>(),
            NullLogger<OverlayLiveCoordinator>.Instance,
            notifier
        );
        await coordinator.StartAsync(CancellationToken.None);
        var firstConnection = await OpenAsync(coordinator, first);
        var otherQueueConnection = await OpenAsync(coordinator, otherQueue);
        var otherHostConnection = await OpenAsync(coordinator, otherHost);
        (await ReadLiveAsync(firstConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.ViewerQueueBaseline>()
            .Envelope.Payload.Animation.ShouldBe("none");
        _ = await ReadLiveAsync(otherQueueConnection);
        _ = await ReadLiveAsync(otherHostConnection);

        foreach (
            var (transition, animation) in new[]
            {
                (PlayQueueOverlayTransition.PartyChanged, "partyChange"),
                (PlayQueueOverlayTransition.ReadyOutcome, "readyOutcome"),
                (PlayQueueOverlayTransition.SelectedNext, "selectedNext"),
                (PlayQueueOverlayTransition.None, "none"),
            }
        )
        {
            await notifier.NotifyAsync(new(1, 10, transition), CancellationToken.None);
            (await ReadLiveAsync(firstConnection))
                .ShouldBeOfType<OverlayLiveTransportMessage.ViewerQueueEvent>()
                .Envelope.Payload.Animation.ShouldBe(animation);
            otherQueueConnection.Messages.TryRead(out _).ShouldBeFalse();
            otherHostConnection.Messages.TryRead(out _).ShouldBeFalse();
        }

        var reconnect = await OpenAsync(coordinator, first);
        (await ReadLiveAsync(reconnect))
            .ShouldBeOfType<OverlayLiveTransportMessage.ViewerQueueBaseline>()
            .Envelope.Payload.Animation.ShouldBe("none");
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public void BrowserDashboardAndHelp_ExposeViewerQueueWithoutPrivateDataLanguage()
    {
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain(".viewer-queue");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("renderViewerQueue");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("getComputedTextLength");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "characters.slice(0, fittingLength).join(\"\") + \"…\""
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "\"data-fit-width\": String(maximumWidth)"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("\"aria-label\": text");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "appendTextClip(definitions, clipPathId, 48, 240 + index * 40, 528, 36)"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "appendTextClip(definitions, clipPathId, 624, 240 + index * 40, 528, 36)"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("partyChange");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("readyOutcome");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("selectedNext");
        var dashboard = File.ReadAllText(SourcePath("Features/Overlays/OverlaysPage.razor"));
        dashboard.ShouldContain("Viewer Queue");
        dashboard.ShouldContain("Current party rows");
        dashboard.ShouldContain("Next rows");
        dashboard.ShouldContain("Preview state");
        dashboard.ShouldContain("data-draft-type");
        var help = File.ReadAllText(SourcePath("Components/Layout/PageHelpButton.razor.cs"));
        help.ShouldContain("Viewer Queue");
        help.ShouldContain("Play with viewers");
        help.ShouldContain("keeping the saved setup and queue");
        help.ShouldNotContain("stable selectors");
    }

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static ConfigurePlayQueueCommand Queue(int capacity, bool showNames) =>
        new(
            "squad",
            "Community squad",
            "Example game",
            capacity,
            true,
            PlayQueueSelectionMode.JoinOrder,
            showNames,
            120,
            30,
            15,
            [
                new("platform", "Platform", ["PC", "Console"]),
                new("region", "Region"),
                new("preferred-role", "Preferred role", ["Tank", "Support"]),
            ],
            []
        );

    private static JoinPlayQueueCommand Join(string login, int index) =>
        new(
            new(login, $"twitch-{login}", login),
            0,
            new Dictionary<string, string> { ["platform"] = "PC", ["region"] = $"Region {index}" }
        );

    private static ResolvedOverlayInstance Instance(
        int hostId,
        int queueId,
        OverlayAppearance? appearance = null
    ) =>
        new(
            hostId,
            Guid.NewGuid(),
            OverlayType.ViewerQueue,
            new OverlayConfiguration.ViewerQueueV1(queueId, 4, 6, appearance),
            new OverlayRevision(1)
        );

    private static PlayQueueService CreateService(
        SqliteBlokeBotDbFactory database,
        PlayQueueChangeNotifier? notifier = null,
        TimeProvider? timeProvider = null
    ) =>
        new(
            database,
            TestEventBus.Create<AppEventKind>(),
            timeProvider ?? TimeProvider.System,
            notifier
        );

    private static PlayQueueResult<T>.Succeeded Success<T>(PlayQueueResult<T> result) =>
        result.Match(
            value => value,
            rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database, string login)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = $"{login}-id",
            Login = login,
            DisplayName = login,
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = await db.Hosts.SingleAsync(value => value.Id == hostId);
        host.EnabledFeatures = features;
        _ = await db.SaveChangesAsync();
    }

    private static async Task<OverlayLiveCoordinator.OverlayLiveConnection> OpenAsync(
        OverlayLiveCoordinator coordinator,
        ResolvedOverlayInstance instance
    ) =>
        (await coordinator.OpenAsync(instance, coordinator.Generation, CancellationToken.None))
            .ShouldBeOfType<OverlayLiveOpenResult.Opened>()
            .Connection;

    private static async Task<OverlayLiveTransportMessage> ReadLiveAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await connection.Messages.ReadAsync(timeout.Token);
    }

    private static string ReplaceNumber(string json, string property, int value)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root[property] = value;
        return root.ToJsonString();
    }

    private static string WithNull(string json, string property)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root[property] = null;
        return root.ToJsonString();
    }

    private static string SourcePath(string relativePath) =>
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
                relativePath
            )
        );

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class CommittedObserver(SqliteBlokeBotDbFactory database)
        : IPlayQueueChangeObserver
    {
        internal List<PlayQueueCommittedChange> Changes { get; } = [];

        internal List<int> EventCounts { get; } = [];

        public async ValueTask PlayQueueChangedAsync(
            PlayQueueCommittedChange change,
            CancellationToken cancellationToken
        )
        {
            await using var db = await database.CreateDbContextAsync(cancellationToken);
            Changes.Add(change);
            EventCounts.Add(
                await db.PlayQueueEvents.CountAsync(
                    value => value.QueueId == change.QueueId,
                    cancellationToken
                )
            );
        }
    }

    private sealed class FixedQueueProvider : IOverlayStateProvider
    {
        public Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration =
                instance.Configuration.ShouldBeOfType<OverlayConfiguration.ViewerQueueV1>();
            return Task.FromResult<OverlaySnapshotProjection>(
                new OverlaySnapshotProjection.ViewerQueueV1(
                    new ViewerQueueV1OverlaySnapshot
                    {
                        ServerEpoch = Guid.Parse("e4c724e5-d113-48e8-a201-ee5579fb44c6"),
                        Sequence = instance.Revision.Value,
                        GeneratedAtUtc = DateTimeOffset.UnixEpoch,
                        Animation = "none",
                        Appearance = configuration.Appearance,
                        State = new PlayQueueOverlayState(
                            "Community squad",
                            "Example game",
                            true,
                            1,
                            [],
                            [new("viewer", [new("platform", "Platform", "PC")])]
                        ),
                    }
                )
            );
        }
    }
}
