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

    public string ContentType { get; set; } = string.Empty;

    public long ByteLength { get; set; }

    public int ContentRevision { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class OverlayCueMediaAssetReference
{
    public long CueId { get; set; }

    public long AssetId { get; set; }

    public int HostId { get; set; }

    public OverlayCue Cue { get; set; } = null!;

    public OverlayMediaAsset Asset { get; set; } = null!;
}
