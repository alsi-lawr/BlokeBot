using BlokeBot.Announcements;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class AnnouncementDeliveryPolicyTests
{
    [Test]
    public void RetryUntilExpiredThenSkipEntity_Mapping_ReturnsImmutableClosedPolicy()
    {
        var retryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2));
        var occurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30));
        var entity = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
        {
            RetryDelay = retryDelay,
            OccurrenceLifetime = occurrenceLifetime,
        };

        var mapped = AnnouncementDeliveryPolicyMapper.ToDomain(entity);

        var timing = mapped.Match(policy => (policy.RetryDelay, policy.OccurrenceLifetime));
        timing.RetryDelay.ShouldBe(retryDelay);
        timing.OccurrenceLifetime.ShouldBe(occurrenceLifetime);
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

        Should.Throw<ArgumentException>(() => AnnouncementDeliveryPolicyMapper.ToDomain(entity));
    }
}
