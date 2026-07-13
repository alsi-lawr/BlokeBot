using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

internal abstract record AnnouncementDeliveryPolicy
{
    private protected AnnouncementDeliveryPolicy() { }

    internal TResult Match<TResult>(
        Func<RetryUntilExpiredThenSkip, TResult> retryUntilExpiredThenSkip
    )
    {
        return this switch
        {
            RetryUntilExpiredThenSkip policy => retryUntilExpiredThenSkip(policy),
            _ => throw new UnreachableException("Unknown announcement delivery policy."),
        };
    }

    internal sealed record RetryUntilExpiredThenSkip : AnnouncementDeliveryPolicy
    {
        internal RetryUntilExpiredThenSkip(
            AnnouncementRetryDelay retryDelay,
            AnnouncementOccurrenceLifetime occurrenceLifetime
        )
        {
            ArgumentNullException.ThrowIfNull(retryDelay);
            ArgumentNullException.ThrowIfNull(occurrenceLifetime);

            if (retryDelay.Value >= occurrenceLifetime.Value)
            {
                throw new ArgumentException(
                    "Retry delay must be less than the occurrence lifetime.",
                    nameof(retryDelay)
                );
            }

            RetryDelay = retryDelay;
            OccurrenceLifetime = occurrenceLifetime;
        }

        internal AnnouncementRetryDelay RetryDelay { get; }

        internal AnnouncementOccurrenceLifetime OccurrenceLifetime { get; }
    }
}

internal static class AnnouncementDeliveryPolicyMapper
{
    internal static AnnouncementDeliveryPolicy ToDomain(
        CustomAnnouncementDeliveryPolicy? policy
    )
    {
        return policy switch
        {
            RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy retry =>
                new AnnouncementDeliveryPolicy.RetryUntilExpiredThenSkip(
                    retry.RetryDelay,
                    retry.OccurrenceLifetime
                ),
            null => throw new InvalidOperationException(
                "A custom announcement delivery policy is required."
            ),
            _ => throw new UnreachableException(
                "Unknown persisted custom announcement delivery policy."
            ),
        };
    }
}
