using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PersistenceLifecycleProjectionTests
{
    [Test]
    public void GuessRoundStates_MappingPersistence_ProduceClosedLifecycleCases()
    {
        var started = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        var closed = started.AddMinutes(5);

        GuessRoundLifecycle
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

        Should.Throw<PersistenceDataIntegrityException>(() =>
            GuessRoundLifecycle.FromPersistence(GuessRoundStatus.Completed, started, closed, null)
        );
    }

    [Test]
    public void GiveawayStates_MappingPersistence_RequireTerminalCompletionTime()
    {
        var completed = new DateTime(2026, 7, 14, 10, 5, 0, DateTimeKind.Utc);

        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Active, completed.AddMinutes(-5), null)
            .ShouldBeOfType<PointsGiveawayLifecycle.Active>();
        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Completed, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Completed>();
        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Cancelled, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Cancelled>();
        PointsGiveawayLifecycle
            .FromPersistence(PointsGiveawayStatus.Expired, completed.AddMinutes(-5), completed)
            .ShouldBeOfType<PointsGiveawayLifecycle.Expired>();

        Should.Throw<PersistenceDataIntegrityException>(() =>
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

        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Stopped, null)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Stopped>();
        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Starting, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Starting>();
        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Started, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Started>();
        HostedChannelRuntimeLifecycle
            .FromPersistence(BotChannelRuntimeState.Stopping, changed)
            .ShouldBeOfType<HostedChannelRuntimeLifecycle.Stopping>();

        Should.Throw<PersistenceDataIntegrityException>(() =>
            HostedChannelRuntimeLifecycle.FromPersistence(BotChannelRuntimeState.Started, null)
        );
    }
}
