namespace BlokeBot.Core.Features.Bounties;

internal sealed record BountyExpirySchedulerPolicy
{
    internal required TimeSpan PollInterval { get; init; }

    internal required int BatchSize { get; init; }

    internal void EnsureValid()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollInterval),
                PollInterval,
                "The bounty expiry scheduler poll interval must be positive."
            );
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchSize),
                BatchSize,
                "The bounty expiry scheduler batch size must be positive."
            );
        }
    }
}
