using BlokeBot.Announcements;

namespace BlokeBot.Persistence.Models;

public enum CustomAnnouncementDeliveryPolicyKind
{
    RetryUntilExpiredThenSkip,
}

public abstract class CustomAnnouncementDeliveryPolicy
{
    private protected CustomAnnouncementDeliveryPolicy() { }

    public int Id { get; set; }

    public int HostId { get; set; }

    public CustomAnnouncement? Announcement { get; set; }
}

public sealed class RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
    : CustomAnnouncementDeliveryPolicy
{
    public required AnnouncementRetryDelay RetryDelay { get; set; }

    public required AnnouncementOccurrenceLifetime OccurrenceLifetime { get; set; }
}
