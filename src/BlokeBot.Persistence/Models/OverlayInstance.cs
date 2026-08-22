namespace BlokeBot.Persistence.Models;

public enum OverlayType
{
    [PersistedToken("empty")]
    Empty,

    [PersistedToken("guessing")]
    Guessing,

    [PersistedToken("cue-player")]
    CuePlayer,

    [PersistedToken("giveaway")]
    Giveaway,

    [PersistedToken("event-feed")]
    EventFeed,

    [PersistedToken("viewer-queue")]
    ViewerQueue,

    [PersistedToken("community-goal")]
    CommunityGoal,

    [PersistedToken("viewer-funded-bounty")]
    ViewerFundedBounty,
}

public enum OverlayEventFeedKind
{
    [PersistedToken("pointAward")]
    PointAward,

    [PersistedToken("guessingWinner")]
    GuessingWinner,

    [PersistedToken("giveawayWinner")]
    GiveawayWinner,

    [PersistedToken("bingoEvent")]
    BingoEvent,

    [PersistedToken("achievementCompletion")]
    AchievementCompletion,
}

public enum OverlayEventFeedPriority
{
    [PersistedToken("normal")]
    Normal,

    [PersistedToken("high")]
    High,
}

public enum OverlayEventFeedLifecycle
{
    [PersistedToken("queued")]
    Queued,

    [PersistedToken("active")]
    Active,

    [PersistedToken("consumed")]
    Consumed,

    [PersistedToken("suppressed")]
    Suppressed,
}

public sealed class OverlayEventFeedItem
{
    public long Id { get; set; }
    public long OverlayInstanceId { get; set; }
    public int HostId { get; set; }
    public OverlayEventFeedKind Kind { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public OverlayEventFeedPriority Priority { get; set; }
    public OverlayEventFeedLifecycle Lifecycle { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime? DisplayDeadlineUtc { get; set; }
    public DateTime? TombstoneExpiresAtUtc { get; set; }
    public OverlayInstance OverlayInstance { get; set; } = null!;
}

public enum OverlayInstanceEventKind
{
    [PersistedToken("created")]
    Created,

    [PersistedToken("renamed")]
    Renamed,

    [PersistedToken("configured")]
    Configured,

    [PersistedToken("enabled")]
    Enabled,

    [PersistedToken("disabled")]
    Disabled,

    [PersistedToken("key-rotated")]
    KeyRotated,

    [PersistedToken("deleted")]
    Deleted,
}

public sealed class OverlayInstance
{
    public long Id { get; set; }

    public Guid PublicId { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public OverlayType Type { get; set; }

    public bool IsEnabled { get; set; }

    public string ConfigurationJson { get; set; } = string.Empty;

    public byte[] AccessKeyDigest { get; set; } = [];

    public int KeyVersion { get; set; }

    public long Revision { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class OverlayInstanceDomainEvent
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public Guid OverlayPublicId { get; set; }

    public int SchemaVersion { get; set; }

    public OverlayInstanceEventKind Kind { get; set; }

    public string ActorUserId { get; set; } = string.Empty;

    public string ActorLogin { get; set; } = string.Empty;

    public long OverlayRevision { get; set; }

    public int KeyVersion { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

public enum OverlayCueQueuePolicy
{
    [PersistedToken("enqueue")]
    Enqueue,

    [PersistedToken("replace")]
    Replace,

    [PersistedToken("ignore")]
    Ignore,

    [PersistedToken("concurrent")]
    Concurrent,
}

public sealed class OverlayCue
{
    public long Id { get; set; }

    public Guid PublicId { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public int DurationMilliseconds { get; set; }

    public OverlayCueQueuePolicy QueuePolicy { get; set; }

    public string ConfigurationJson { get; set; } = string.Empty;

    public long Revision { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class OverlayMediaAsset
{
    public long Id { get; set; }

    public Guid PublicId { get; set; }

    public int HostId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ContentRevision { get; set; }

    public Guid DocumentId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public OverlayMediaDocument Document { get; set; } = null!;
}

public enum OverlayMediaDocumentState
{
    [PersistedToken("publishing")]
    Publishing,

    [PersistedToken("available")]
    Available,

    [PersistedToken("unavailable")]
    Unavailable,

    [PersistedToken("orphaned")]
    Orphaned,
}

public sealed class OverlayMediaDocument
{
    public Guid Id { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long ByteLength { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public OverlayMediaDocumentState State { get; set; }

    public int? LegacyHostId { get; set; }

    public string? LegacyStorageKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? OrphanedAtUtc { get; set; }

    public List<OverlayMediaAsset> References { get; set; } = [];
}

public sealed class OverlayCueMediaAssetReference
{
    public long CueId { get; set; }

    public long AssetId { get; set; }

    public int HostId { get; set; }

    public OverlayCue Cue { get; set; } = null!;

    public OverlayMediaAsset Asset { get; set; } = null!;
}
