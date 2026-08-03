using BlokeBot.Core.Features.HostedChannels.Authorization;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostBotOAuthStateStoreTests
{
    [Test]
    public void IssuedState_ConsumingForBoundUserAndHost_IsSingleUse()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 21, 20, 0, 0, TimeSpan.Zero));
        var states = new HostBotOAuthStateStore(time);
        var state = states.Issue("owner-id", 42);

        var consumed = states.Consume(state, "owner-id");
        var replay = states.Consume(state, "owner-id");

        HostBotOAuthStateStore.IsHostBotState(state).ShouldBeTrue();
        consumed.ShouldBe(new HostBotOAuthStateConsumption.Consumed(42));
        _ = replay.ShouldBeOfType<HostBotOAuthStateConsumption.Rejected>();
    }

    [Test]
    public void IssuedState_ConsumingForDifferentUser_RejectsAndConsumesState()
    {
        var states = new HostBotOAuthStateStore(TimeProvider.System);
        var state = states.Issue("owner-id", 42);

        _ = states
            .Consume(state, "other-user-id")
            .ShouldBeOfType<HostBotOAuthStateConsumption.Rejected>();
        _ = states
            .Consume(state, "owner-id")
            .ShouldBeOfType<HostBotOAuthStateConsumption.Rejected>();
    }

    [Test]
    public void ExpiredState_Consuming_Rejects()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 21, 20, 0, 0, TimeSpan.Zero));
        var states = new HostBotOAuthStateStore(time);
        var state = states.Issue("owner-id", 42);
        time.Advance(HostBotOAuthStateStore.Lifetime);

        _ = states
            .Consume(state, "owner-id")
            .ShouldBeOfType<HostBotOAuthStateConsumption.Rejected>();
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
