namespace BlokeBot.Persistence.Models;

public enum CommunitySeasonStatus
{
    [PersistedToken("Draft")]
    Draft,

    [PersistedToken("Open")]
    Open,

    [PersistedToken("Closed")]
    Closed,

    [PersistedToken("Archived")]
    Archived,
}

public enum CommunityVisibility
{
    [PersistedToken("Public")]
    Public,

    [PersistedToken("Hidden")]
    Hidden,
}

public enum CommunityDefinitionKind
{
    [PersistedToken("Quest")]
    Quest,

    [PersistedToken("Achievement")]
    Achievement,
}

public enum CommunityProgressScope
{
    [PersistedToken("Viewer")]
    Viewer,

    [PersistedToken("Communal")]
    Communal,
}

public enum CommunityCompletionMode
{
    [PersistedToken("OneTime")]
    OneTime,

    [PersistedToken("Repeatable")]
    Repeatable,
}

public enum CommunityEventRuleKind
{
    [PersistedToken("ChatMessage")]
    ChatMessage,

    [PersistedToken("Follow")]
    Follow,

    [PersistedToken("Subscription")]
    Subscription,

    [PersistedToken("Cheer")]
    Cheer,

    [PersistedToken("IncomingRaid")]
    IncomingRaid,

    [PersistedToken("RewardRedemption")]
    RewardRedemption,

    [PersistedToken("BountyCompleted")]
    BountyCompleted,

    [PersistedToken("ExternalGrant")]
    ExternalGrant,
}

public enum CommunityProgressIncrement
{
    [PersistedToken("Occurrence")]
    Occurrence,

    [PersistedToken("EventValue")]
    EventValue,
}

public enum CommunityResetCadence
{
    [PersistedToken("None")]
    None,

    [PersistedToken("Daily")]
    Daily,

    [PersistedToken("Weekly")]
    Weekly,
}

public enum CommunityRewardKind
{
    [PersistedToken("Title")]
    Title,

    [PersistedToken("Badge")]
    Badge,

    [PersistedToken("CosmeticAccent")]
    CosmeticAccent,
}

public enum CommunityRolloverKind
{
    [PersistedToken("Timer")]
    Timer,

    [PersistedToken("Restart")]
    Restart,

    [PersistedToken("ScheduleEdit")]
    ScheduleEdit,
}

public enum CommunityEventKind
{
    [PersistedToken("SeasonOpened")]
    SeasonOpened,

    [PersistedToken("SeasonClosed")]
    SeasonClosed,

    [PersistedToken("SeasonArchived")]
    SeasonArchived,

    [PersistedToken("ProgressAdvanced")]
    ProgressAdvanced,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("PeriodRolledOver")]
    PeriodRolledOver,

    [PersistedToken("RewardGranted")]
    RewardGranted,

    [PersistedToken("RewardEquipped")]
    RewardEquipped,
}

public sealed class CommunitySeason
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public Guid CreationOperationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ModeratorNotes { get; set; } = string.Empty;
    public CommunitySeasonStatus Status { get; set; }
    public CommunityVisibility Visibility { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public long Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<CommunityDefinition> Definitions { get; set; } = [];
    public List<CommunityRewardDefinition> Rewards { get; set; } = [];
}

public sealed class CommunityDefinition
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public long SeasonId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CommunityDefinitionKind Kind { get; set; }
    public CommunityProgressScope Scope { get; set; }
    public CommunityCompletionMode CompletionMode { get; set; }
    public CommunityEventRuleKind EventRule { get; set; }
    public CommunityProgressIncrement Increment { get; set; }
    public string? FilterToken { get; set; }
    public long Target { get; set; }
    public string PointsReward { get; set; } = "0";
    public CommunityResetCadence ResetCadence { get; set; }
    public string ResetLocalTime { get; set; } = "00:00";
    public int? ResetWeekday { get; set; }
    public int ScheduleRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public CommunitySeason Season { get; set; } = null!;
    public List<CommunityDefinitionReward> Rewards { get; set; } = [];
}

public sealed class CommunityRewardDefinition
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public long SeasonId { get; set; }
    public string Key { get; set; } = string.Empty;
    public CommunityRewardKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PresentationToken { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public CommunitySeason Season { get; set; } = null!;
}

public sealed class CommunityDefinitionReward
{
    public long DefinitionId { get; set; }
    public long RewardDefinitionId { get; set; }
    public CommunityDefinition Definition { get; set; } = null!;
    public CommunityRewardDefinition RewardDefinition { get; set; } = null!;
}

public sealed class CommunityProgress
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long SeasonId { get; set; }
    public long DefinitionId { get; set; }
    public string SubjectKey { get; set; } = string.Empty;
    public string? ViewerTwitchUserId { get; set; }
    public string? ViewerLogin { get; set; }
    public string? ViewerDisplayName { get; set; }
    public long Amount { get; set; }
    public int CompletionCount { get; set; }
    public string? PeriodKey { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CommunityCompletion
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public long SeasonId { get; set; }
    public long DefinitionId { get; set; }
    public string SubjectKey { get; set; } = string.Empty;
    public string? ViewerTwitchUserId { get; set; }
    public string? ViewerLogin { get; set; }
    public string? ViewerDisplayName { get; set; }
    public string DefinitionKey { get; set; } = string.Empty;
    public string DefinitionName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string? PeriodKey { get; set; }
    public string PointsGranted { get; set; } = "0";
    public string RewardSnapshot { get; set; } = "[]";
    public string SourceOperationKey { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
}

public sealed class CommunityRewardUnlock
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long RewardDefinitionId { get; set; }
    public string ViewerTwitchUserId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public string ViewerDisplayName { get; set; } = string.Empty;
    public long CompletionId { get; set; }
    public DateTime GrantedAtUtc { get; set; }
}

public sealed class CommunityEquippedReward
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public CommunityRewardKind Kind { get; set; }
    public long RewardDefinitionId { get; set; }
    public string ViewerTwitchUserId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public Guid LastOperationId { get; set; }
    public DateTime EquippedAtUtc { get; set; }
}

public sealed class CommunitySourceEventReceipt
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public CommunityEventRuleKind SourceKind { get; set; }
    public string SourceEventId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}

public sealed class CommunityExternalGrantReceipt
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public long? CompletionId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

public sealed class CommunityResetPeriod
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long DefinitionId { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public CommunityRolloverKind RolloverKind { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CommunitySeasonStanding
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long SeasonId { get; set; }
    public string ViewerTwitchUserId { get; set; } = string.Empty;
    public string ViewerLogin { get; set; } = string.Empty;
    public string ViewerDisplayName { get; set; } = string.Empty;
    public int CompletedCount { get; set; }
    public long ProgressAmount { get; set; }
    public int Rank { get; set; }
    public DateTime SnapshottedAtUtc { get; set; }
}

public sealed class CommunityAudit
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long? SeasonId { get; set; }
    public long? DefinitionId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    public string ActorTwitchUserId { get; set; } = string.Empty;
    public string ActorLogin { get; set; } = string.Empty;
    public string PrivateNote { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class CommunityDomainEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long? SeasonId { get; set; }
    public CommunityEventKind Kind { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string PublicPayload { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; }
}
