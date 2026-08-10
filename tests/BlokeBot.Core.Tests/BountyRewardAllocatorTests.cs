using System.Numerics;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BountyRewardAllocatorTests
{
    private static readonly DateTime _now = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ProportionalSplit_PreservesTotalAndAssignsRemainderDeterministically()
    {
        var result = BountyRewardAllocator.Allocate(
            [
                new BountyContribution("3", "third", 1, _now),
                new BountyContribution("1", "first", 1, _now),
                new BountyContribution("2", "second", 1, _now),
            ],
            new BigInteger(5),
            BountyRewardDistribution.Proportional
        );

        result
            .Aggregate(BigInteger.Zero, (total, share) => total + share.Amount)
            .ShouldBe(new BigInteger(5));
        result
            .ToDictionary(share => share.TwitchUserId, share => share.Amount)
            .ShouldBe(
                new Dictionary<string, BigInteger>
                {
                    ["1"] = 2,
                    ["2"] = 2,
                    ["3"] = 1,
                }
            );
    }

    [Test]
    public void EqualSplit_GroupsRepeatPledgesByLoginAccount()
    {
        var result = BountyRewardAllocator.Allocate(
            [
                new BountyContribution("new-id", "first", 3, _now.AddMinutes(1)),
                new BountyContribution("old-id", "first", 2, _now),
                new BountyContribution("2", "second", 5, _now),
            ],
            new BigInteger(5),
            BountyRewardDistribution.Equal
        );

        result.ShouldBe([
            new BountyRewardShare("new-id", "first", 3),
            new BountyRewardShare("2", "second", 2),
        ]);
    }
}
