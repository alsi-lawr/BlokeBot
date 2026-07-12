namespace BlokeBot.Persistence.Models;

public sealed class PublicChatOutboxMessage
{
    public long Id { get; set; }

    public required string Channel { get; set; }

    public string? Message { get; set; }

    public required string DeduplicationKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public PublicChatOutboxStatus Status { get; set; } = PublicChatOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public Guid? ClaimToken { get; set; }

    public int? ClaimSlot { get; set; }

    public DateTime? ClaimExpiresAtUtc { get; set; }

    public DateTime? SendStartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

public enum PublicChatOutboxStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Claimed")]
    Claimed,

    [PersistedToken("Sending")]
    Sending,

    [PersistedToken("Delivered")]
    Delivered,

    [PersistedToken("Faulted")]
    Faulted,
}
