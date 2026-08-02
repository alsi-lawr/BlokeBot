using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class GuessingOverlayTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 31, 2, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Projection_CoversNoOpenClosedCompletedAndBothParentSwitches()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(database);
        var epoch = new OverlayServerEpoch();
        var provider = new OverlayStateProvider(database, epoch, new FixedTimeProvider(_now));
        var instance = Instance(seeded.HostId);

        (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
            .Snapshot.State.ShouldBeOfType<GuessingV1OverlayPresentationState.NoRound>();

        int roundId;
        await using (var db = await database.CreateDbContextAsync())
        {
            var round = new GuessRound
            {
                HostId = seeded.HostId,
                GuessRoundProfileId = seeded.ProfileId,
                Status = GuessRoundStatus.Open,
                StartedAtUtc = _now.UtcDateTime.AddMinutes(-5),
                Votes =
                [
                    Vote("nightowl", "Blue", -4),
                    Vote("newviewer", "Blue", -3),
                    Vote("other", "Red", -2),
                ],
            };
            db.Rounds.Add(round);
            await db.SaveChangesAsync();
            roundId = round.Id;
        }

        var open = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
            .Snapshot;
        open.ServerEpoch.ShouldBe(epoch.Value);
        open.Sequence.ShouldBe(instance.Revision.Value);
        open.ResultDurationMilliseconds.ShouldBe(9000);
        open.State.ShouldBeOfType<GuessingV1OverlayPresentationState.Open>()
            .ShouldSatisfyAllConditions(
                state => state.RoundName.ShouldBe("Match winner"),
                state => state.GuessCount.ShouldBe(3),
                state => state.ClosesAtUtc.ShouldBeNull()
            );

        await UpdateRoundAsync(
            database,
            roundId,
            GuessRoundStatus.Closed,
            _now.UtcDateTime.AddMinutes(-1),
            null
        );
        (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
            .Snapshot.State.ShouldBeOfType<GuessingV1OverlayPresentationState.Closed>()
            .GuessCount.ShouldBe(3);

        await UpdateRoundAsync(
            database,
            roundId,
            GuessRoundStatus.Completed,
            _now.UtcDateTime,
            "Blue"
        );
        var completed = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
            .Snapshot.State.ShouldBeOfType<GuessingV1OverlayPresentationState.Completed>();
        completed.WinningAnswer.ShouldBe("Blue");
        completed.Winners.ShouldBe(["nightowl", "newviewer"]);
        completed.AwardedPointsPerWinner.ShouldBe("250");
        completed.PointLabel.ShouldBe("beans");

        await SetFeaturesAsync(database, seeded.HostId, HostFeatureFlags.Overlays);
        (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
        await SetFeaturesAsync(database, seeded.HostId, HostFeatureFlags.Guessing);
        (
            await provider.ProjectAsync(instance, CancellationToken.None)
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();

        await SetFeaturesAsync(
            database,
            seeded.HostId,
            HostFeatureFlags.Overlays | HostFeatureFlags.Guessing
        );
        var restored = (await provider.ProjectAsync(instance, CancellationToken.None))
            .ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
            .Snapshot.State.ShouldBeOfType<GuessingV1OverlayPresentationState.Completed>();
        restored.RoundName.ShouldBe(completed.RoundName);
        restored.GuessCount.ShouldBe(completed.GuessCount);
        restored.WinningAnswer.ShouldBe(completed.WinningAnswer);
        restored.Winners.ShouldBe(completed.Winners);
        restored.AwardedPointsPerWinner.ShouldBe(completed.AwardedPointsPerWinner);
        restored.PointLabel.ShouldBe(completed.PointLabel);
        restored.CompletedAtUtc.ShouldBe(completed.CompletedAtUtc);
    }

    [Test]
    public async Task PreviewSamples_AreTypedCompleteAndParentGated()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedAsync(database);
        var provider = new OverlayStateProvider(
            database,
            new OverlayServerEpoch(),
            new FixedTimeProvider(_now)
        );
        var instance = Instance(seeded.HostId);

        var states = new List<GuessingV1OverlayPresentationState>();
        foreach (var sample in Enum.GetValues<GuessingOverlaySampleState>())
        {
            states.Add(
                (await provider.ProjectSampleAsync(instance, sample, CancellationToken.None))
                    .ShouldBeOfType<OverlaySnapshotProjection.GuessingV1>()
                    .Snapshot.State
            );
        }

        states
            .Select(state => state.Phase)
            .ShouldBe([
                GuessingOverlayPhase.NoRound,
                GuessingOverlayPhase.Open,
                GuessingOverlayPhase.Closed,
                GuessingOverlayPhase.Completed,
            ]);

        await SetFeaturesAsync(database, seeded.HostId, HostFeatureFlags.Overlays);
        (
            await provider.ProjectSampleAsync(
                instance,
                GuessingOverlaySampleState.Completed,
                CancellationToken.None
            )
        ).ShouldBeOfType<OverlaySnapshotProjection.Unavailable>();
    }

    [Test]
    public async Task LiveTransitions_AreHostScopedCoalescedAndReconnectWithoutResultReplay()
    {
        var events = TestEventBus.Create<AppEventKind>();
        var provider = new BlockingGuessingProvider();
        await using var coordinator = new OverlayLiveCoordinator(
            new OverlayServerEpoch(),
            provider,
            new FixedTimeProvider(_now),
            events,
            NullLogger<OverlayLiveCoordinator>.Instance
        );
        await coordinator.StartAsync(CancellationToken.None);
        var owner = Instance(hostId: 71);
        var other = Instance(hostId: 72);
        var ownerConnection = await OpenAsync(coordinator, owner);
        var otherConnection = await OpenAsync(coordinator, other);
        (await ReadAsync(ownerConnection))
            .ShouldBeOfType<OverlayLiveTransportMessage.GuessingBaseline>()
            .Envelope.Payload.Animation.ShouldBe("none");
        await ReadAsync(otherConnection);

        provider.SetPhase(owner.HostId, GuessingOverlayPhase.Closed);
        var blockedProjection = provider.BlockNextProjection();
        await coordinator.GuessingChangedAsync(owner.HostId, CancellationToken.None);
        await blockedProjection.Entered;
        await coordinator.GuessingChangedAsync(owner.HostId, CancellationToken.None);
        await coordinator.GuessingChangedAsync(owner.HostId, CancellationToken.None);
        blockedProjection.Release();

        var closed = (
            await ReadAsync(ownerConnection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GuessingEvent>();
        var coalesced = (
            await ReadAsync(ownerConnection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GuessingEvent>();
        closed.Envelope.Payload.Animation.ShouldBe("statusChange");
        closed.Envelope.Sequence.ShouldBe(1);
        coalesced.Envelope.Payload.Animation.ShouldBe("none");
        coalesced.Envelope.Sequence.ShouldBe(2);
        otherConnection.Messages.TryRead(out _).ShouldBeFalse();

        provider.SetPhase(owner.HostId, GuessingOverlayPhase.Completed);
        await coordinator.GuessingChangedAsync(owner.HostId, CancellationToken.None);
        var result = (
            await ReadAsync(ownerConnection)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GuessingEvent>();
        result.Envelope.Payload.Animation.ShouldBe("result");
        result.Envelope.Payload.ResultDurationMilliseconds.ShouldBe(9000);

        await events.PublishAsync(AppEventKind.HostedChannelsChanged, CancellationToken.None);
        var terminal = await ReadTerminalAsync(ownerConnection);
        terminal.EventType.ShouldBe("reauthenticate");
        await coordinator.GuessingChangedAsync(owner.HostId, CancellationToken.None);
        ownerConnection.Messages.TryRead(out _).ShouldBeFalse();

        var reconnected = await OpenAsync(coordinator, owner);
        var baseline = (
            await ReadAsync(reconnected)
        ).ShouldBeOfType<OverlayLiveTransportMessage.GuessingBaseline>();
        baseline.Envelope.Payload.State.ShouldBeOfType<GuessingV1OverlayPresentationState.Completed>();
        baseline.Envelope.Payload.Animation.ShouldBe("none");
        reconnected.Messages.TryRead(out _).ShouldBeFalse();
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Test]
    public void ClientAndDashboard_EncodeStableStatesReducedMotionAndAllSamples()
    {
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain(
            "@media (prefers-reduced-motion: reduce)"
        );
        OverlayBrowserSourceAssets.Stylesheet.ShouldContain("animation: none");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("state.phase === \"noRound\"");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("state.phase === \"completed\"");
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "typeof projection.animation === \"string\""
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain(
            "applyPresentationAnimation(\"none\", 0, fromDraft)"
        );
        OverlayBrowserSourceAssets.JavaScript.ShouldContain("if (!fromDraft)");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("style.setProperty");
        OverlayBrowserSourceAssets.JavaScript.ShouldNotContain("api.twitch.tv");

        var dashboard = File.ReadAllText(SourcePath("OverlaysPage.razor"));
        dashboard.ShouldContain("Enum.GetValues<GuessingOverlaySampleState>()");
        dashboard.ShouldContain("data-overlay-disabled-recovery");
        dashboard.ShouldContain("Turn Guessing game on in Channel setup");
        dashboard.ShouldContain("Show the number of guesses");
        dashboard.ShouldContain("Result animation duration");
    }

    private static ResolvedOverlayInstance Instance(int hostId) =>
        new ResolvedOverlayInstance(
            hostId,
            Guid.NewGuid(),
            OverlayType.Guessing,
            new OverlayConfiguration.GuessingV1(true, 9),
            new OverlayRevision(3)
        );

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

    private static async Task<OverlayLiveControlEnvelope> ReadTerminalAsync(
        OverlayLiveCoordinator.OverlayLiveConnection connection
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await connection.Messages.WaitToReadAsync(timeout.Token))
        {
            while (connection.Messages.TryRead(out _)) { }
        }
        connection.TryTakeTerminal(out var terminal).ShouldBeTrue();
        return terminal.ShouldNotBeNull();
    }

    private static async Task<ProjectionSeed> SeedAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "host-id",
            Login = "host",
            DisplayName = "Host",
            EnabledFeatures = HostFeatureFlags.Overlays | HostFeatureFlags.Guessing,
            CreatedAtUtc = _now.UtcDateTime,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Match winner",
            Slug = "match-winner",
            IsDefault = true,
            WinningGuessPointReward = "250",
        };
        db.Profiles.Add(profile);
        db.PointsSettings.Add(new PointsSettings { HostId = host.Id, PointLabel = "beans" });
        await db.SaveChangesAsync();
        return new ProjectionSeed(host.Id, profile.Id);
    }

    private static GuessVote Vote(string login, string answer, int minutes) =>
        new GuessVote
        {
            Login = login,
            GuessName = answer,
            GuessedAtUtc = _now.UtcDateTime.AddMinutes(minutes),
        };

    private static async Task UpdateRoundAsync(
        SqliteBlokeBotDbFactory database,
        int roundId,
        GuessRoundStatus status,
        DateTime? closedAtUtc,
        string? winningName
    )
    {
        await using var db = await database.CreateDbContextAsync();
        await db
            .Rounds.Where(round => round.Id == roundId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(round => round.Status, status)
                    .SetProperty(round => round.ClosedAtUtc, closedAtUtc)
                    .SetProperty(round => round.WinningName, winningName)
            );
    }

    private static async Task SetFeaturesAsync(
        SqliteBlokeBotDbFactory database,
        int hostId,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        await db
            .Hosts.Where(host => host.Id == hostId)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(host => host.EnabledFeatures, features)
            );
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

    private sealed record ProjectionSeed(int HostId, int ProfileId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BlockingGuessingProvider : IOverlayStateProvider
    {
        private readonly Dictionary<int, GuessingOverlayPhase> _phases = [];
        private TaskCompletionSource? _blockedProjectionEntered;
        private TaskCompletionSource? _blockedProjectionRelease;

        internal void SetPhase(int hostId, GuessingOverlayPhase phase) => _phases[hostId] = phase;

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

        public async Task<OverlaySnapshotProjection> ProjectAsync(
            ResolvedOverlayInstance instance,
            CancellationToken cancellationToken
        )
        {
            if (_blockedProjectionEntered is { } entered)
            {
                var release = _blockedProjectionRelease.ShouldNotBeNull();
                _blockedProjectionEntered = null;
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                _blockedProjectionRelease = null;
            }

            var phase = _phases.GetValueOrDefault(instance.HostId, GuessingOverlayPhase.Open);
            GuessingV1OverlayPresentationState state = phase switch
            {
                GuessingOverlayPhase.NoRound => new GuessingV1OverlayPresentationState.NoRound(),
                GuessingOverlayPhase.Open => new GuessingV1OverlayPresentationState.Open
                {
                    RoundName = "Match winner",
                    GuessCount = 3,
                    ClosesAtUtc = null,
                },
                GuessingOverlayPhase.Closed => new GuessingV1OverlayPresentationState.Closed
                {
                    RoundName = "Match winner",
                    GuessCount = 3,
                    ClosedAtUtc = _now,
                },
                GuessingOverlayPhase.Completed => new GuessingV1OverlayPresentationState.Completed
                {
                    RoundName = "Match winner",
                    GuessCount = 3,
                    WinningAnswer = "Blue",
                    Winners = ["nightowl"],
                    AwardedPointsPerWinner = "250",
                    PointLabel = "beans",
                    CompletedAtUtc = _now,
                },
                _ => throw new ArgumentOutOfRangeException(),
            };
            return new OverlaySnapshotProjection.GuessingV1(
                new GuessingV1OverlaySnapshot
                {
                    ServerEpoch = Guid.Parse("e8d384cb-912f-4736-993c-1c86ad4c60ef"),
                    Sequence = instance.Revision.Value,
                    GeneratedAtUtc = _now,
                    ResultDurationMilliseconds = 9000,
                    State = state,
                }
            );
        }
    }
}
