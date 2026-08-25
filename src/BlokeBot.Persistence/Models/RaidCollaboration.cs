namespace BlokeBot.Persistence.Models;

public sealed class RaidCollaborationSettings
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public bool WelcomeEnabled { get; set; } = true;
    public string WelcomeMessage { get; set; } = RaidCollaborationDefaults.WelcomeMessage;
    public int DeduplicationWindowMinutes { get; set; } = 60;
    public string Language { get; set; } = "en";
    public string EligibleCategories { get; set; } = string.Empty;
    public int RelationshipCooldownHours { get; set; } = 336;
    public bool IncludeFollowedLiveChannels { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ApprovedRaidChannel
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string? TwitchUserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ApprovedClipId { get; set; }
    public DateTime ApprovedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class RaidCollaborationHistoryEntry
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public RaidDirection Direction { get; set; }
    public string OtherTwitchUserId { get; set; } = string.Empty;
    public string OtherLogin { get; set; } = string.Empty;
    public string OtherDisplayName { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public string? Category { get; set; }
    public string? ProviderStreamId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public RaidWelcomeOutcome WelcomeOutcome { get; set; } = RaidWelcomeOutcome.NotConfigured;
    public RaidShoutoutOutcome ShoutoutOutcome { get; set; } = RaidShoutoutOutcome.NotConfigured;
    public DateTime RecordedAtUtc { get; set; }
}

public enum RaidDirection
{
    [PersistedToken("Incoming")]
    Incoming,

    [PersistedToken("Outgoing")]
    Outgoing,
}

public enum RaidWelcomeOutcome
{
    [PersistedToken("NotConfigured")]
    NotConfigured,

    [PersistedToken("Deduplicated")]
    Deduplicated,

    [PersistedToken("Delivered")]
    Delivered,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("Suppressed")]
    Suppressed,
}

public enum RaidShoutoutOutcome
{
    [PersistedToken("NotConfigured")]
    NotConfigured,

    [PersistedToken("Deduplicated")]
    Deduplicated,

    [PersistedToken("Queued")]
    Queued,

    [PersistedToken("Sent")]
    Sent,

    [PersistedToken("Cooldown")]
    Cooldown,

    [PersistedToken("NotEligible")]
    NotEligible,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("Suppressed")]
    Suppressed,
}

public static class RaidCollaborationDefaults
{
    public const string WelcomeMessage = "Welcome {display_name} and community!";
}
