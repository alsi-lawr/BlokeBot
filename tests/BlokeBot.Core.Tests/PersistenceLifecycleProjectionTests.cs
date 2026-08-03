using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PersistenceLifecycleProjectionTests
{
    [Test]
    public void GuessRoundStates_MappingPersistence_ProduceClosedLifecycleCases()
    {
        var started = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var closed = started.AddMinutes(5);

        _ = GuessRoundLifecycle
            .FromPersistence(GuessRoundStatus.Open, started, null, null)
            .ShouldBeOfType<GuessRoundLifecycle.Open>();
        GuessRoundLifecycle
            .FromPersistence(GuessRoundStatus.Closed, started, closed, null)
            .ShouldBeOfType<GuessRoundLifecycle.Closed>()
            .ClosedAtUtc.ShouldBe(closed);
        GuessRoundLifecycle
            .FromPersistence(GuessRoundStatus.Completed, started, closed, "blue")
            .ShouldBeOfType<GuessRoundLifecycle.Completed>()
            .WinningName.ShouldBe("blue");

        _ = Should.Throw<PersistenceDataIntegrityException>(() =>
            GuessRoundLifecycle.FromPersistence(GuessRoundStatus.Completed, started, closed, null)
        );
    }

    [Test]
    public void GiveawayStates_MappingPersistence_RequireTerminalCompletionTime()
    {
        var completed = new DateTime(2026, 7, 14, 10, 5, 0, DateTimeKind.Utc);

        _ = PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Active, completed.AddMinutes(-5), null)
            .ShouldBeOfType<PointsGiveawayLifecycle.Active>();
        _ = PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Completed, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Completed>();
        _ = PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Cancelled, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Cancelled>();
        _ = PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Expired, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Expired>();

        _ = Should.Throw<PersistenceDataIntegrityException>(() =>
            PointsGiveawayLifecycle.FromPersistence(
                PointsGiveawayStatus.Completed,
                completed.AddMinutes(-5),
                null
            )
        );
    }

    [Test]
    public void RuntimeStates_MappingPersistence_RequireTransitionTimesWhenActive()
    {
        var changed = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

        _ = HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Stopped, null)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Stopped>();
        _ = HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Starting, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Starting>();
        _ = HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Started, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Started>();
        _ = HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Stopping, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Stopping>();

        _ = Should.Throw<PersistenceDataIntegrityException>(() =>
            HostedChannelRuntimeLifecycle.FromPersistence(BotChannelRuntimeState.Started, null)
        );
    }
}
