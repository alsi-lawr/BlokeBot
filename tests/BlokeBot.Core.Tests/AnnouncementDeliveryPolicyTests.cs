using BlokeBot.Announcements;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AnnouncementDeliveryPolicyTests
{
    [Test]
    public void EntityMapping_ReturnsValidatedPolicyWithTiming()
    {
        var retryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2));
        var occurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30));
        var entity = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            RetryDelay = retryDelay,
            OccurrenceLifetime = occurrenceLifetime,
        };

        var mapped = AnnouncementDeliveryPolicyMapper.ToDomain(entity);

        mapped.RetryDelay.ShouldBe(retryDelay);
        mapped.OccurrenceLifetime.ShouldBe(occurrenceLifetime);
    }

    [Test]
    public void MissingEntity_Mapping_ThrowsRequiredPolicyError()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            AnnouncementDeliveryPolicyMapper.ToDomain(null)
        );

        exception.Message.ShouldContain("required");
    }

    [Test]
    public void InternallyInconsistentTiming_Mapping_IsRejected()
    {
        var entity = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(30)),
            OccurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30)),
        };

        _ = Should.Throw<ArgumentException>(() =>
            AnnouncementDeliveryPolicyMapper.ToDomain(entity)
        );
    }
}
