using System.Diagnostics;
using BlokeBot.Announcements;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.CustomCommands;

internal sealed record AnnouncementDeliveryPolicy
{
    internal AnnouncementDeliveryPolicy(
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

internal static class AnnouncementDeliveryPolicyMapper
{
    internal static AnnouncementDeliveryPolicy ToDomain(CustomAnnouncementDeliveryPolicy? policy)
    {
        return policy switch
        {
            RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy retry =>
                new AnnouncementDeliveryPolicy(retry.RetryDelay, retry.OccurrenceLifetime),
            null => throw new InvalidOperationException(
                "A custom announcement delivery policy is required."
            ),
            _ => throw new UnreachableException(
                "Unknown persisted custom announcement delivery policy."
            ),
        };
    }
}
