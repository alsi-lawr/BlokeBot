namespace BlokeBot.Persistence.Models;

public enum PlayQueueSelectionMode
{
    [PersistedToken("JoinOrder")]
    JoinOrder,

    [PersistedToken("LeastRecentParticipation")]
    LeastRecentParticipation,
}

public enum PlayQueueEntryStatus
{
    [PersistedToken("Waiting")]
    Waiting,

    [PersistedToken("AwaitingReady")]
    AwaitingReady,

    [PersistedToken("Ready")]
    Ready,

    [PersistedToken("Selected")]
    Selected,

    [PersistedToken("Left")]
    Left,

    [PersistedToken("Skipped")]
    Skipped,

    [PersistedToken("NoShow")]
    NoShow,
}

public enum PlayQueueEventKind
{
    [PersistedToken("QueueConfigured")]
    QueueConfigured,

    [PersistedToken("Joined")]
    Joined,

    [PersistedToken("Left")]
    Left,

    [PersistedToken("ReadyCheckStarted")]
    ReadyCheckStarted,

    [PersistedToken("Ready")]
    Ready,

    [PersistedToken("NoShow")]
    NoShow,

    [PersistedToken("PartySelected")]
    PartySelected,

    [PersistedToken("Skipped")]
    Skipped,

    [PersistedToken("QueueClosed")]
    QueueClosed,
}

public sealed class PlayQueue
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ActivityName { get; set; } = string.Empty;
    public int Capacity { get; set; } = 4;
    public bool IsOpen { get; set; }
    public PlayQueueSelectionMode SelectionMode { get; set; }
    public bool ShowParticipantNames { get; set; }
    public int ReadinessTimeoutSeconds { get; set; } = 120;
    public int HistoryRetentionDays { get; set; } = 30;
    public int SkipExclusionMinutes { get; set; } = 15;
    public int CurrentPartyNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<PlayQueueField> Fields { get; set; } = [];
    public List<PlayQueueRoleRequirement> RoleRequirements { get; set; } = [];
    public List<PlayQueueEntry> Entries { get; set; } = [];
}

public sealed class PlayQueueField
{
    public int Id { get; set; }
    public int QueueId { get; set; }
    public PlayQueue? Queue { get; set; }
    public int Position { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Choices { get; set; } = string.Empty;
}

public sealed class PlayQueueRoleRequirement
{
    public int Id { get; set; }
    public int QueueId { get; set; }
    public PlayQueue? Queue { get; set; }
    public string Role { get; set; } = string.Empty;
    public int MinimumCount { get; set; }
}

public sealed class PlayQueueEntry
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public int QueueId { get; set; }
    public PlayQueue? Queue { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public string? TwitchUserId { get; set; }
    public string NormalizedLogin { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Priority { get; set; }
    public PlayQueueEntryStatus Status { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ReadyExpiresAtUtc { get; set; }
    public int? PartyNumber { get; set; }
    public string PrivateModeratorNote { get; set; } = string.Empty;
    public List<PlayQueueEntryValue> Values { get; set; } = [];
}

public sealed class PlayQueueEntryValue
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public PlayQueueEntry? Entry { get; set; }
    public int FieldId { get; set; }
    public PlayQueueField? Field { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class PlayQueueParticipation
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public int QueueId { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public DateTime ParticipatedAtUtc { get; set; }
}

public sealed class PlayQueueExclusion
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public int QueueId { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string PrivateReason { get; set; } = string.Empty;
}

public sealed class PlayQueueDomainEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public int QueueId { get; set; }
    public long? EntryId { get; set; }
    public int SchemaVersion { get; set; }
    public PlayQueueEventKind Kind { get; set; }
    public string PublicPayload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
