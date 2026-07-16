namespace BlokeBot.Core.Features.Points.Giveaways;

internal sealed record PointsGiveawaySchedulerRecoveryPolicy
{
    internal required TimeSpan RetryDelay { get; init; }

    internal void EnsureValid()
    {
        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryDelay),
                RetryDelay,
                "The giveaway scheduler retry delay cannot be negative."
            );
        }
    }
}
