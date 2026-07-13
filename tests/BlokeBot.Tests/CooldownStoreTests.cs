using BlokeBot.Features.CustomCommands;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CooldownStoreTests
{
    [Test]
    public void CustomCommandCooldownAtBoundary_Recording_PrunesExpiredKeysAndAllowsReuse()
    {
        var clock = new ManualTimeProvider();
        var store = new CustomCommandCooldownStore(clock);

        store.TryRecord(1, CustomCommandCooldownScope.User, "alice", TimeSpan.FromSeconds(10))
            .ShouldBeTrue();
        store.TryRecord(2, CustomCommandCooldownScope.Global, "bob", TimeSpan.FromSeconds(5))
            .ShouldBeTrue();
        clock.Advance(TimeSpan.FromSeconds(5));
        store.TryRecord(1, CustomCommandCooldownScope.User, "alice", TimeSpan.FromSeconds(10))
            .ShouldBeFalse();
        store.EntryCount.ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(5));
        store.TryRecord(1, CustomCommandCooldownScope.User, "alice", TimeSpan.FromSeconds(10))
            .ShouldBeTrue();
        store.EntryCount.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(10));
        store.TryRecord(99, CustomCommandCooldownScope.Global, "", TimeSpan.Zero)
            .ShouldBeTrue();
        store.EntryCount.ShouldBe(0);
    }

    [Test]
    public void GamblingCooldownAtBoundary_Recording_PrunesExpiredKeysAndAllowsReuse()
    {
        var clock = new ManualTimeProvider();
        var store = new PointsGamblingCooldownStore(clock);

        store.TryRecord(1, "alice", TimeSpan.FromSeconds(10)).ShouldBeTrue();
        store.TryRecord(1, "bob", TimeSpan.FromSeconds(5)).ShouldBeTrue();
        clock.Advance(TimeSpan.FromSeconds(5));
        store.TryRecord(1, "alice", TimeSpan.FromSeconds(10)).ShouldBeFalse();
        store.EntryCount.ShouldBe(1);

        clock.Advance(TimeSpan.FromSeconds(5));
        store.TryRecord(1, "alice", TimeSpan.FromSeconds(10)).ShouldBeTrue();
        store.EntryCount.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(10));
        store.TryRecord(1, "unused", TimeSpan.Zero).ShouldBeTrue();
        store.EntryCount.ShouldBe(0);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan value)
        {
            _now += value;
        }
    }
}
