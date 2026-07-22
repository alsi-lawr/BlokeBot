namespace BlokeBot.Persistence.Models;

public sealed class ReplyPinPolicy
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public required string Feature { get; set; }
    public required string ReplyKey { get; set; }
    public int? DurationSeconds { get; set; }
    public bool UnpinOnOwnerCompletion { get; set; }
}

public sealed class PublicChatPinOperation
{
    public long Id { get; set; }
    public PublicChatPinOperationKind Kind { get; set; }
    public PublicChatPinOperationStatus Status { get; set; }
    public long? OutboxMessageId { get; set; }
    public int HostId { get; set; }
    public required string Channel { get; set; }
    public required string Feature { get; set; }
    public required string ReplyKey { get; set; }
    public long OwnerId { get; set; }
    public int? DurationSeconds { get; set; }
    public bool UnpinOnOwnerCompletion { get; set; }
    public required string TwitchMessageId { get; set; }
    public string? PinnerTwitchUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AttemptStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Outcome { get; set; }
}

public sealed class ActivePublicChatPin
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public required string Channel { get; set; }
    public required string TwitchMessageId { get; set; }
    public required string PinnerTwitchUserId { get; set; }
    public required string Feature { get; set; }
    public required string ReplyKey { get; set; }
    public long OwnerId { get; set; }
    public bool UnpinOnOwnerCompletion { get; set; }
    public DateTime PinnedAtUtc { get; set; }
}

public enum PublicChatPinOperationKind
{
    [PersistedToken("Pin")]
    Pin,

    [PersistedToken("Unpin")]
    Unpin,
}

public enum PublicChatPinOperationStatus
{
    [PersistedToken("AwaitingDelivery")]
    AwaitingDelivery,

    [PersistedToken("Ready")]
    Ready,

    [PersistedToken("Attempting")]
    Attempting,

    [PersistedToken("Succeeded")]
    Succeeded,

    [PersistedToken("NoOp")]
    NoOp,

    [PersistedToken("Terminal")]
    Terminal,
}
