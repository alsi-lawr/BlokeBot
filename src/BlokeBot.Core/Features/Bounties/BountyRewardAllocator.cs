using System.Numerics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bounties;

internal sealed record BountyContribution(
    string TwitchUserId,
    string Login,
    BigInteger Amount,
    DateTime ContributedAtUtc
);

internal sealed record BountyRewardShare(string TwitchUserId, string Login, BigInteger Amount);

internal static class BountyRewardAllocator
{
    public static IReadOnlyList<BountyRewardShare> Allocate(
        IReadOnlyList<BountyContribution> contributions,
        BigInteger reward,
        BountyRewardDistribution distribution
    )
    {
        if (reward.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reward));
        }

        var contributors = contributions
            .GroupBy(contribution => contribution.Login, StringComparer.Ordinal)
            .Select(group => new BountyContribution(
                group
                    .OrderByDescending(contribution => contribution.ContributedAtUtc)
                    .ThenBy(contribution => contribution.TwitchUserId, StringComparer.Ordinal)
                    .First()
                    .TwitchUserId,
                group.Key,
                group.Aggregate(
                    BigInteger.Zero,
                    (total, contribution) => total + contribution.Amount
                ),
                group.Max(contribution => contribution.ContributedAtUtc)
            ))
            .Where(contribution => contribution.Amount.Sign > 0)
            .OrderBy(contribution => contribution.Login, StringComparer.Ordinal)
            .ToArray();

        return reward.IsZero || contributors.Length == 0
            ? []
            : distribution switch
            {
                BountyRewardDistribution.Equal => AllocateEqual(contributors, reward),
                BountyRewardDistribution.Proportional => AllocateProportional(contributors, reward),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(distribution),
                    distribution,
                    null
                ),
            };
    }

    private static IReadOnlyList<BountyRewardShare> AllocateEqual(
        IReadOnlyList<BountyContribution> contributors,
        BigInteger reward
    )
    {
        var quotient = BigInteger.DivRem(reward, contributors.Count, out var remainder);
        return contributors
            .Select(
                (contributor, index) =>
                    new BountyRewardShare(
                        contributor.TwitchUserId,
                        contributor.Login,
                        quotient + (index < remainder ? BigInteger.One : BigInteger.Zero)
                    )
            )
            .Where(share => share.Amount.Sign > 0)
            .ToArray();
    }

    private static IReadOnlyList<BountyRewardShare> AllocateProportional(
        IReadOnlyList<BountyContribution> contributors,
        BigInteger reward
    )
    {
        var total = contributors.Aggregate(
            BigInteger.Zero,
            (sum, contribution) => sum + contribution.Amount
        );
        var allocations = contributors
            .Select(contributor =>
            {
                var numerator = reward * contributor.Amount;
                var amount = BigInteger.DivRem(numerator, total, out var remainder);
                return new ProportionalAllocation(contributor, amount, remainder);
            })
            .ToArray();
        var allocated = allocations.Aggregate(
            BigInteger.Zero,
            (sum, allocation) => sum + allocation.Amount
        );
        var remaining = reward - allocated;
        var remainderOrder = allocations
            .OrderByDescending(allocation => allocation.Remainder)
            .ThenBy(allocation => allocation.Contribution.Login, StringComparer.Ordinal)
            .Select((allocation, index) => new { allocation.Contribution.Login, index })
            .ToDictionary(value => value.Login, value => value.index, StringComparer.Ordinal);

        return allocations
            .Select(allocation =>
            {
                var receivesRemainder = remainderOrder[allocation.Contribution.Login] < remaining;
                return new BountyRewardShare(
                    allocation.Contribution.TwitchUserId,
                    allocation.Contribution.Login,
                    allocation.Amount + (receivesRemainder ? BigInteger.One : BigInteger.Zero)
                );
            })
            .Where(share => share.Amount.Sign > 0)
            .ToArray();
    }

    private sealed record ProportionalAllocation(
        BountyContribution Contribution,
        BigInteger Amount,
        BigInteger Remainder
    );
}
