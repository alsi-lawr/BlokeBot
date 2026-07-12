namespace BlokeBot.Persistence.Models;

public sealed class PublicChatOutboxMessage
{
    public long Id { get; set; }

    public required string Channel { get; set; }

    public string? Message { get; set; }

    public string? DeduplicationKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public PublicChatOutboxStatus Status { get; set; } = PublicChatOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public int SafePreSendFailureCount { get; set; }

    public Guid? ClaimToken { get; set; }

    public int? ClaimSlot { get; set; }

    public DateTime? ClaimExpiresAtUtc { get; set; }

    public DateTime? SendStartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public PublicChatOutboxFailurePhase? FailurePhase { get; set; }

    public string? FailureType { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? RejectionCode { get; set; }
}

public enum PublicChatOutboxStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Claimed")]
    Claimed,

    [PersistedToken("Sending")]
    Sending,

    [PersistedToken("SafePreSendTransient")]
    SafePreSendTransient,

    [PersistedToken("SafePreSendScheduling")]
    SafePreSendScheduling,

    [PersistedToken("SafePreSendExhausted")]
    SafePreSendExhausted,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("Ambiguous")]
    Ambiguous,

    [PersistedToken("Unexpected")]
    Unexpected,
}

public sealed class PublicChatSendReceipt
{
    public long OutboxMessageId { get; set; }

    public DateTime AttemptedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? DeliveredDeduplicationKey { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }
}

public enum PublicChatOutboxFailurePhase
{
    [PersistedToken("Preparation")]
    Preparation,

    [PersistedToken("Send")]
    Send,
}
