namespace BlokeBot.Persistence.Models;

public sealed class CustomAnnouncement
{
    public int Id { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int MessageLibraryEntryId { get; set; }

    public int DeliveryPolicyId { get; set; }

    public DateTime? LastSentAtUtc { get; set; }

    public DateTime? LastOccurrenceAtUtc { get; set; }

    public AnnouncementOccurrenceStatus OccurrenceStatus { get; set; }

    public DateTime? OccurrenceDueAtUtc { get; set; }

    public DateTime? OccurrenceExpiresAtUtc { get; set; }

    public DateTime? OccurrenceNextAttemptAtUtc { get; set; }

    public DateTime? OccurrenceCompletedAtUtc { get; set; }

    public int OccurrenceAttemptCount { get; set; }

    public string? OccurrenceMessage { get; set; }

    public int ChatMessagesSinceLastSent { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public CustomMessageLibraryEntry? MessageLibraryEntry { get; set; }

    public CustomAnnouncementSchedule Schedule { get; set; } = null!;

    public CustomAnnouncementDeliveryPolicy DeliveryPolicy { get; set; } = null!;
}

public enum AnnouncementOccurrenceStatus
{
    [PersistedToken("None")]
    None,

    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Attempting")]
    Attempting,

    [PersistedToken("RetryScheduled")]
    RetryScheduled,

    [PersistedToken("Accepted")]
    Accepted,

    [PersistedToken("SkippedExpired")]
    SkippedExpired,

    [PersistedToken("TerminalRejected")]
    TerminalRejected,

    [PersistedToken("TerminalAmbiguous")]
    TerminalAmbiguous,

    [PersistedToken("TerminalUnexpected")]
    TerminalUnexpected,

    [PersistedToken("TerminalInvalidTimeZone")]
    TerminalInvalidTimeZone,

    [PersistedToken("TerminalMissingMessage")]
    TerminalMissingMessage,
}
