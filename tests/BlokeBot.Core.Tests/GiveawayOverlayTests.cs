using System.Text.Json;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class GiveawayOverlayTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Configuration_IsStrictBoundedAndUsesBothRequiredParents()
    {
        OverlayConfiguration
            .Parse(
                OverlayType.Giveaway,
                """{"schemaVersion":1,"title":"Community giveaway","showEntrantCount":true,"showCountdown":false,"showJoinCommand":true}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Valid>()
            .Value.ShouldBeOfType<OverlayConfiguration.GiveawayV1>()
            .Title.ShouldBe("Community giveaway");
        _ = OverlayConfiguration
            .Parse(
                OverlayType.Giveaway,
                $$"""{"schemaVersion":1,"title":"{{new string('x', 81)}}","showEntrantCount":true,"showCountdown":false,"showJoinCommand":true}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();
        _ = OverlayConfiguration
            .Parse(
                OverlayType.Giveaway,
                """{"schemaVersion":1,"title":"Giveaway","showEntrantCount":true,"showCountdown":false,"showJoinCommand":true,"joinCommand":"enter"}"""
            )
            .ShouldBeOfType<OverlayConfigurationParseResult.Invalid>();

        OverlayRequiredFeatures
            .AreEnabled(OverlayType.Giveaway, HostFeatureFlags.Overlays | HostFeatureFlags.Points)
            .ShouldBeTrue();
        OverlayRequiredFeatures
            .AreEnabled(OverlayType.Giveaway, HostFeatureFlags.Overlays)
            .ShouldBeFalse();
        OverlayRequiredFeatures
            .AreEnabled(OverlayType.Giveaway, HostFeatureFlags.Points)
            .ShouldBeFalse();
    }

    [Test]
    public async Task GiveawayChangeNotifier_FansOutTheCommittedHostIdentity()
    {
        var observer = new RecordingGiveawayObserver();
        var notifier = new PointsGiveawayChangeNotifier(
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()),
            [observer]
        );

        await notifier.NotifyChangedAsync(73, CancellationToken.None);

        observer.HostIds.ShouldBe([73]);
    }

    [Test]
    public async Task Projection_CoversLifecycleCanonicalAliasPrivacyAndBothParents()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(database);
        var provider = new OverlayStateProvider(
            database,
            new OverlayServerEpoch(),
            new FixedTimeProvider(_now)
        );
        var instance = Instance(hostId);

        _ = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot.State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Idle>();

        int giveawayId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var giveaway = new PointsGiveaway
            {
                HostId = hostId,
                Status = PointsGiveawayStatus.Active,
                StartedAtUtc = _now.UtcDateTime.AddMinutes(-1),
                EndsAtUtc = _now.UtcDateTime.AddMinutes(4),
                Entrants =
                [
                    new PointsGiveawayEntrant
                    {
                        Login = "private-entrant",
                        JoinedAtUtc = _now.UtcDateTime,
                    },
                ],
            };
            _ = db.PointsGiveaways.Add(giveaway);
            _ = await db.SaveChangesAsync();
            giveawayId = giveaway.Id;
        }

        var open = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot.State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Open>();
        open.EntrantCount.ShouldBe(1);
        open.JoinCommand.ShouldBe("!enter");
        open.ClosesAtUtc.ShouldBe(_now.AddMinutes(4));
        var openJson = JsonSerializer.Serialize(open);
        openJson.ShouldNotContain("private-entrant");
        openJson.ToLowerInvariant().ShouldNotContain("eligibility");

        await UpdateAsync(
            database,
            giveawayId,
            PointsGiveawayStatus.Active,
            _now.UtcDateTime.AddSeconds(-1),
            null
        );
        _ = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot.State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Ending>();

        await using (var db = await database.CreateDbContextAsync())
        {
            var giveaway = await db
                .PointsGiveaways.Include(value => value.Winners)
                .SingleAsync(value => value.Id == giveawayId);
            giveaway.Status = PointsGiveawayStatus.Completed;
            giveaway.CompletedAtUtc = _now.UtcDateTime;
            giveaway.Winners.Add(new PointsGiveawayWinner { Login = "winner-one", Payout = "500" });
            giveaway.Winners.Add(new PointsGiveawayWinner { Login = "winner-two", Payout = "250" });
            _ = await db.SaveChangesAsync();
        }
        var completed = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot.State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Completed>();
        completed.Winners.Select(value => value.Login).ShouldBe(["winner-one", "winner-two"]);
        completed.Winners.Select(value => value.AwardedPoints).ShouldBe(["500", "250"]);
        completed.PointLabel.ShouldBe("beans");

        await UpdateAsync(
            database,
            giveawayId,
            PointsGiveawayStatus.Cancelled,
            _now.UtcDateTime,
            _now.UtcDateTime
        );
        (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot.State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Cancelled>()
            .Message.ShouldBe("Giveaway cancelled");

        await UpdateAsync(
            database,
            giveawayId,
            PointsGiveawayStatus.Expired,
            _now.UtcDateTime,
            _now.UtcDateTime
        );
        (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
            .Snapshot.State.ShouldBeOfType<GiveawayV1OverlayPresentationState.Cancelled>()
            .Message.ShouldBe("Giveaway closed without a winner");

        await SetFeaturesAsync(database, hostId, HostFeatureFlags.Overlays);
        _ = (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
        await SetFeaturesAsync(database, hostId, HostFeatureFlags.Points);
        _ = (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
    }

    [Test]
    public async Task SamplesAndLiveCompletion_AreTypedHostScopedAndNeverReplayWinnerAnimation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(database);
        var provider = new OverlayStateProvider(
            database,
            new OverlayServerEpoch(),
            new FixedTimeProvider(_now)
        );
        var instance = Instance(hostId);
        var samples = new List<GiveawayOverlayPhase>();
        foreach (var sample in Enum.GetValues<GiveawayOverlaySampleState>())
        {
            samples.Add(
                (await provider.ProjectSampleAsync(instance, sample, CancellationToken.None))
                    .ShouldBeOfType<OverlaySnapshotProjection.GiveawayV1>()
                    .Snapshot.State.Phase
            );
        }
        samples.ShouldBe([
            GiveawayOverlayPhase.Idle,
            GiveawayOverlayPhase.Open,
            GiveawayOverlayPhase.Ending,
            GiveawayOverlayPhase.Completed,
            GiveawayOverlayPhase.Cancelled,
        ]);

        var liveProvider = new GiveawayLiveProvider();
        await using var coordinator = new OverlayLiveCoordinator(
            new OverlayServerEpoch(),
            liveProvider,
            new FixedTimeProvider(_now),
            TestEventBus.Create<AppEventKind>(),
            NullLogger<OverlayLiveCoordinator>.Instance
        );
        var owner = Instance(81);
        var other = Instance(82);
        var ownerConnection = await OpenAsync(coordinator, owner);
        var otherConnection = await OpenAsync(coordinator, other);
        (await ReadAsync(ownerConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.GiveawayBaseline>()
            .Envelope.Payload.Animation.ShouldBe("none");
        _ = await ReadAsync(otherConnection);

        var blockedProjection = liveProvider.BlockNextProjection();
        await coordinator.GiveawayChangedAsync(81, CancellationToken.None);
        await blockedProjection.Entered;
        await coordinator.GiveawayChangedAsync(81, CancellationToken.None);
        await coordinator.GiveawayChangedAsync(81, CancellationToken.None);
        blockedProjection.Release();
        _ = (
            await ReadAsync(ownerConnection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GiveawayEvent>();
        _ = (
            await ReadAsync(ownerConnection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GiveawayEvent>();
        ownerConnection.Messages.TryRead(out _).ShouldBeFalse();
        otherConnection.Messages.TryRead(out _).ShouldBeFalse();

        liveProvider.SetPhase(81, GiveawayOverlayPhase.Completed);
        await coordinator.GiveawayChangedAsync(81, CancellationToken.None);
        var winner = (
            await ReadAsync(ownerConnection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GiveawayEvent>();
        winner.Envelope.Payload.Animation.ShouldBe("winner");
        winner.Envelope.Payload.WinnerAnimationDurationMilliseconds.ShouldBe(5000);
        otherConnection.Messages.TryRead(out _).ShouldBeFalse();

        await coordinator.GiveawayChangedAsync(81, CancellationToken.None);
        (await ReadAsync(ownerConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.GiveawayEvent>()
            .Envelope.Payload.Animation.ShouldBe("none");

        var reconnected = await OpenAsync(coordinator, owner);
        (await ReadAsync(reconnected))
            .ShouldBeOfType<OverlayLiveTransportMessage.GiveawayBaseline>()
            .Envelope.Payload.Animation.ShouldBe("none");
    }

    [Test]
    public void ClientDashboardAndHelp_EncodeCountdownSamplesPrivacyAndReducedMotion()
    {
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain(
            "@media (prefers-reduced-motion: reduce)"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("validGiveawayState");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("giveawayCountdownTimer");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("setInterval");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("animation !== \"winner\"");

        var dashboard = File.ReadAllText(SourcePath("OverlaySourcesPanel.razor"));
        dashboard.ShouldContain("Enum.GetValues<GiveawayOverlaySampleState>()");
        dashboard.ShouldContain("Entrant count");
        dashboard.ShouldContain("Close-time countdown");
        dashboard.ShouldContain("Current join command");
        dashboard.ShouldContain("never published");

        var help = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(SourcePath("OverlaySourcesPanel.razor"))
                        .ShouldNotBeNull(),
                    "..",
                    "..",
                    "Components",
                    "Layout",
                    "PageHelpButton.razor.cs"
                )
            )
        );
        help.ShouldContain("Giveaway overlay availability");
        help.ShouldContain("without replaying suppressed updates");
    }

    private static ResolvedOverlayInstance Instance(int hostId) =>
        new ResolvedOverlayInstance(
            hostId,
            Guid.NewGuid(),
            OverlayType.Giveaway,
            new OverlayConfiguration.GiveawayV1("Community giveaway", true, true, true),
            new OverlayRevision(4)
        );

    private static async Task<int> SeedAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = Guid.NewGuid().ToString("N"),
            Login = $"host-{Guid.NewGuid():N}",
            DisplayName = "Host",
            EnabledFeatures = HostFeatureFlags.Overlays | HostFeatureFlags.Points,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointsSettings.Add(new PointsSettings { HostId = host.Id, PointLabel = "beans" });
        db.CommandAliases.AddRange(
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Join,
                Alias = "join",
            },
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Join,
                Alias = "enter",
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task UpdateAsync(
        SqliteBlokeBotDbFactory database,
        int giveawayId,
        PointsGiveawayStatus status,
        DateTime endsAtUtc,
        DateTime? completedAtUtc
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = await db
            .PointsGiveaways.Where(value => value.Id == giveawayId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(value => value.Status, status)
                    .SetProperty(value => value.EndsAtUtc, endsAtUtc)
                    .SetProperty(value => value.CompletedAtUtc, completedAtUtc)
            );
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        _ = await db
            .Hosts.Where(value => value.Id == hostId)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(value => value.EnabledFeatures, features)
            );
    }

    private static async Task<OverlayLiveCoordinator.OverlayLiveConnection> OpenAsync(
        OverlayLiveCoordinator coordinator,
        ResolvedOverlayInstance instance
    ) =>
        (await coordinator.OpenAsync(instance, coordinator.Generation, CancellationToken.None))
            .ShouldBeOfType<OverlayLiveOpenResult.Opened>()
            .Connection;

    private static async Task<OverlayLiveTransportMessage> ReadAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await connection.Messages.ReadAsync(timeout.Token);
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GiveawayLiveProvider : IOverlayStateProvider
    {
        private readonly Dictionary<int, GiveawayOverlayPhase> _phases = [];
        private TaskCompletionSource? _blockedProjectionEntered;
        private TaskCompletionSource? _blockedProjectionRelease;

        internal (Task Entered, Action Release) BlockNextProjection()
        {
            var entered = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var release = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _blockedProjectionEntered = entered;
            _blockedProjectionRelease = release;
            return (entered.Task, () => release.TrySetResult());
        }

        internal void SetPhase(int hostId, GiveawayOverlayPhase phase) => _phases[hostId] = phase;

        public async Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            if (_blockedProjectionEntered is { } entered)
            {
                var release = _blockedProjectionRelease.ShouldNotBeNull();
                _blockedProjectionEntered = null;
                _ = entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                _blockedProjectionRelease = null;
            }

            GiveawayV1OverlayPresentationState state =
                _phases.GetValueOrDefault(instance.HostId, GiveawayOverlayPhase.Open)
                is GiveawayOverlayPhase.Completed
                    ? new GiveawayV1OverlayPresentationState.Completed
                    {
                        Title = "Community giveaway",
                        Winners =
                        [
                            new GiveawayWinnerPresentation
                            {
                                Login = "winner",
                                AwardedPoints = "500",
                            },
                        ],
                        PointLabel = "beans",
                        CompletedAtUtc = _now,
                    }
                    : new GiveawayV1OverlayPresentationState.Open
                    {
                        Title = "Community giveaway",
                        EntrantCount = 3,
                        ClosesAtUtc = _now.AddMinutes(2),
                        JoinCommand = "!enter",
                    };
            return new OverlaySnapshotProjection.GiveawayV1(
                new GiveawayV1OverlaySnapshot
                {
                    ServerEpoch = Guid.Parse("ad22a44b-2214-4058-8fa4-d57cf995b84d"),
                    Sequence = instance.Revision.Value,
                    GeneratedAtUtc = _now,
                    State = state,
                }
            );
        }
    }

    private sealed class RecordingGiveawayObserver : IPointsGiveawayChangeObserver
    {
        internal List<int> HostIds { get; } = [];

        public ValueTask GiveawayChangedAsync(int hostId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HostIds.Add(hostId);
            return ValueTask.CompletedTask;
        }
    }
}
