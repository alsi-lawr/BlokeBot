namespace BlokeBot.Persistence.Models;

public sealed class AutomaticRaidShoutoutSettings
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public bool Enabled { get; set; }
    public int MinimumViewerCount { get; set; } = 1;
    public AutomaticRaidShoutoutMechanism Mechanism { get; set; } =
        AutomaticRaidShoutoutMechanism.Native;
    public AutomaticRaidChatPresentation ChatPresentation { get; set; } =
        AutomaticRaidChatPresentation.Regular;
    public string MessageTemplate { get; set; } = AutomaticRaidShoutoutDefaults.MessageTemplate;
    public int? PinDurationSeconds { get; set; }
    public TwitchAnnouncementColor AnnouncementColor { get; set; } =
        TwitchAnnouncementColor.Primary;
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AutomaticRaidProcessedEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class AutomaticRaidShoutoutOutcome
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public string SourceTwitchUserId { get; set; } = string.Empty;
    public string SourceLogin { get; set; } = string.Empty;
    public string SourceDisplayName { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public AutomaticRaidShoutoutOutcomeStatus Status { get; set; } =
        AutomaticRaidShoutoutOutcomeStatus.Processing;
    public AutomaticRaidShoutoutResultCode? ResultCode { get; set; }
    public DateTime MessageTimestampUtc { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public enum AutomaticRaidShoutoutMechanism
{
    [PersistedToken("Native")]
    Native,

    [PersistedToken("Chat")]
    Chat,
}

public enum AutomaticRaidChatPresentation
{
    [PersistedToken("Regular")]
    Regular,

    [PersistedToken("Pinned")]
    Pinned,

    [PersistedToken("Announcement")]
    Announcement,
}

public enum AutomaticRaidShoutoutOutcomeStatus
{
    [PersistedToken("Processing")]
    Processing,

    [PersistedToken("Delivered")]
    Delivered,

    [PersistedToken("NotDelivered")]
    NotDelivered,

    [PersistedToken("Ambiguous")]
    Ambiguous,
}

public enum AutomaticRaidShoutoutResultCode
{
    [PersistedToken("Delivered")]
    Delivered,

    [PersistedToken("RuntimeMessageTooLong")]
    RuntimeMessageTooLong,

    [PersistedToken("NotReady")]
    NotReady,

    [PersistedToken("AuthorityRequired")]
    AuthorityRequired,

    [PersistedToken("Cooldown")]
    Cooldown,

    [PersistedToken("Invalid")]
    Invalid,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("RateLimited")]
    RateLimited,

    [PersistedToken("PartialFailure")]
    PartialFailure,

    [PersistedToken("Unexpected")]
    Unexpected,

    [PersistedToken("Ambiguous")]
    Ambiguous,
}

public static class AutomaticRaidShoutoutDefaults
{
    public const string MessageTemplate = "Welcome {display_name}! Check them out at {channel_url}";
}
