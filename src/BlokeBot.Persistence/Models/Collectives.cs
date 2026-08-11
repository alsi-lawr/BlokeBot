namespace BlokeBot.Persistence.Models;

public enum CollectiveMembershipRole
{
    [PersistedToken("Coordinator")]
    Coordinator,

    [PersistedToken("Participant")]
    Participant,
}

public enum CollectiveMembershipStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Active")]
    Active,

    [PersistedToken("Declined")]
    Declined,

    [PersistedToken("Left")]
    Left,

    [PersistedToken("Revoked")]
    Revoked,
}

public enum CollectiveWorkflowStatus
{
    [PersistedToken("Pending")]
    Pending,

    [PersistedToken("Active")]
    Active,

    [PersistedToken("Completed")]
    Completed,
}

public enum CollectiveRaidHandoffStatus
{
    [PersistedToken("Prepared")]
    Prepared,

    [PersistedToken("ProviderRejected")]
    ProviderRejected,

    [PersistedToken("Confirmed")]
    Confirmed,
}

public enum CollectiveLocalNotification
{
    [PersistedToken("Moderators")]
    Moderators,

    [PersistedToken("ModeratorsAndOwner")]
    ModeratorsAndOwner,
}

public enum CollectiveAuditAction
{
    [PersistedToken("Created")]
    Created,

    [PersistedToken("HostInvited")]
    HostInvited,

    [PersistedToken("InvitationWithdrawn")]
    InvitationWithdrawn,

    [PersistedToken("InvitationAccepted")]
    InvitationAccepted,

    [PersistedToken("InvitationDeclined")]
    InvitationDeclined,

    [PersistedToken("MemberLeft")]
    MemberLeft,

    [PersistedToken("MemberRevoked")]
    MemberRevoked,

    [PersistedToken("CoordinationTransferred")]
    CoordinationTransferred,

    [PersistedToken("TournamentReferenceChanged")]
    TournamentReferenceChanged,

    [PersistedToken("RaidRelayChanged")]
    RaidRelayChanged,

    [PersistedToken("RaidHandoffPrepared")]
    RaidHandoffPrepared,

    [PersistedToken("RaidHandoffConfirmed")]
    RaidHandoffConfirmed,

    [PersistedToken("GoalChanged")]
    GoalChanged,

    [PersistedToken("GoalSourceChanged")]
    GoalSourceChanged,

    [PersistedToken("GoalProgressChanged")]
    GoalProgressChanged,

    [PersistedToken("LocalSettingsChanged")]
    LocalSettingsChanged,
}

public sealed class Collective
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid CreationOperationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<CollectiveMembership> Memberships { get; set; } = [];
    public CollectiveTournamentReference? TournamentReference { get; set; }
    public CollectiveRaidRelay? RaidRelay { get; set; }
    public CollectiveGoal? Goal { get; set; }
    public List<CollectiveAudit> Audits { get; set; } = [];
}

public sealed class CollectiveMembership
{
    public long Id { get; set; }
    public long CollectiveId { get; set; }
    public int HostId { get; set; }
    public CollectiveMembershipRole Role { get; set; }
    public CollectiveMembershipStatus Status { get; set; }
    public DateTime AcceptWorkAfterUtc { get; set; }
    public DateTime InvitedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Collective Collective { get; set; } = null!;
    public BotHost Host { get; set; } = null!;
}

public sealed class CollectiveTournamentReference
{
    public long Id { get; set; }
    public long CollectiveId { get; set; }
    public int OwnerHostId { get; set; }
    public Guid CompetitionPublicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CompetitionFormat Format { get; set; }
    public CompetitionStatus Status { get; set; }
    public int Round { get; set; }
    public int EntrantCount { get; set; }
    public int ConfirmedResultCount { get; set; }
    public long Revision { get; set; }
    public DateTime LastSourceEventAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Collective Collective { get; set; } = null!;
}

public sealed class CollectiveRaidRelay
{
    public long Id { get; set; }
    public long CollectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CurrentHostId { get; set; }
    public int? NextHostId { get; set; }
    public int AggregateViewerCount { get; set; }
    public CollectiveWorkflowStatus Status { get; set; }
    public long Revision { get; set; }
    public DateTime LastSourceEventAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Collective Collective { get; set; } = null!;
    public List<CollectiveRaidHandoff> Handoffs { get; set; } = [];
}

public sealed class CollectiveRaidHandoff
{
    public long Id { get; set; }
    public long CollectiveRaidRelayId { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public int FromHostId { get; set; }
    public int ToHostId { get; set; }
    public int AggregateViewerCount { get; set; }
    public CollectiveRaidHandoffStatus Status { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public CollectiveRaidRelay RaidRelay { get; set; } = null!;
}

public sealed class CollectiveGoal
{
    public long Id { get; set; }
    public long CollectiveId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public long Target { get; set; }
    public long Current { get; set; }
    public DateTime DeadlineUtc { get; set; }
    public CollectiveWorkflowStatus Status { get; set; }
    public long Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Collective Collective { get; set; } = null!;
    public List<CollectiveGoalHostTotal> HostTotals { get; set; } = [];
}

public sealed class CollectiveGoalHostTotal
{
    public long Id { get; set; }
    public long CollectiveGoalId { get; set; }
    public int HostId { get; set; }
    public Guid SourceBountyPublicId { get; set; }
    public long Total { get; set; }
    public DateTime LastSourceEventAtUtc { get; set; }
    public CollectiveGoal Goal { get; set; } = null!;
}

public sealed class CollectiveLocalSetting
{
    public long Id { get; set; }
    public long CollectiveId { get; set; }
    public int HostId { get; set; }
    public CollectiveLocalNotification Notification { get; set; }
    public long Revision { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Collective Collective { get; set; } = null!;
}

public sealed class CollectiveAudit
{
    public long Id { get; set; }
    public long CollectiveId { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public CollectiveAuditAction Action { get; set; }
    public int ActingHostId { get; set; }
    public int? AffectedHostId { get; set; }
    public string ActorTwitchUserId { get; set; } = string.Empty;
    public string ActorLogin { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public Collective Collective { get; set; } = null!;
}
