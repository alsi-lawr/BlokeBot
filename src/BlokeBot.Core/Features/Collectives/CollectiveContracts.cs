using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Collectives;

public readonly record struct CollectiveId(Guid Value);

public sealed record CollectiveAuthority(
    int SelectedHostId,
    string TwitchUserId,
    string Login,
    bool CanManageSelectedHost
);

public sealed record CreateCollectiveCommand(
    Guid OperationId,
    string Name,
    CollectiveAuthority Authority
);

public sealed record CollectiveMembershipCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    int AffectedHostId,
    CollectiveAuthority Authority
);

public sealed record CollectiveSelfMembershipCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    CollectiveAuthority Authority
);

public sealed record SetTournamentReferenceCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    int OwnerHostId,
    Guid CompetitionPublicId,
    CollectiveAuthority Authority
);

public sealed record ConfigureRaidRelayCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    string Name,
    int CurrentHostId,
    int? NextHostId,
    CollectiveAuthority Authority
);

public sealed record ConfirmRaidHandoffCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    long ExpectedRevision,
    CollectiveAuthority Authority
);

public sealed record CollectiveGoalSource(int HostId, Guid BountyPublicId);

public sealed record ConfigureCollectiveGoalCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    string Name,
    string UnitName,
    long Target,
    DateTime DeadlineUtc,
    IReadOnlyList<CollectiveGoalSource> Sources,
    CollectiveAuthority Authority
);

public sealed record SetCollectiveGoalSourceCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    Guid BountyPublicId,
    CollectiveAuthority Authority
);

public sealed record SaveCollectiveLocalSettingsCommand(
    Guid OperationId,
    CollectiveId CollectiveId,
    long ExpectedRevision,
    CollectiveLocalNotification Notification,
    CollectiveAuthority Authority
);

public abstract record CollectiveMutationOutcome
{
    private CollectiveMutationOutcome() { }

    public sealed record Succeeded(CollectiveId CollectiveId, bool WasIdempotent = false)
        : CollectiveMutationOutcome;

    public sealed record FeatureDisabled(int HostId) : CollectiveMutationOutcome;

    public sealed record AuthorityRequired : CollectiveMutationOutcome;

    public sealed record NotFound : CollectiveMutationOutcome;

    public sealed record Invalid(string Message) : CollectiveMutationOutcome;

    public sealed record Conflict(string Message) : CollectiveMutationOutcome;

    public sealed record LastCoordinatorRequired : CollectiveMutationOutcome;

    public sealed record ProviderRejected : CollectiveMutationOutcome;
}

public abstract record CollectiveDashboardOutcome
{
    private CollectiveDashboardOutcome() { }

    public sealed record Loaded(CollectiveWorkspace Workspace) : CollectiveDashboardOutcome;

    public sealed record FeatureDisabled : CollectiveDashboardOutcome;

    public sealed record HostNotFound : CollectiveDashboardOutcome;
}

public sealed record CollectiveWorkspace(
    IReadOnlyList<CollectiveSummary> Collectives,
    IReadOnlyList<CollectiveKnownHost> KnownHosts,
    IReadOnlyList<CollectiveBountyChoice> OwnedBounties,
    CollectiveId? SelectedCollectiveId,
    CollectiveDashboard? SelectedCollective
);

public sealed record CollectiveKnownHost(int HostId, string Login, string DisplayName);

public sealed record CollectiveBountyChoice(Guid PublicId, string Title);

public sealed record CollectiveSummary(
    CollectiveId Id,
    string Name,
    int ActiveHostCount,
    int PendingHostCount,
    int WorkflowCount,
    DateTime UpdatedAtUtc
);

public sealed record CollectiveDashboard(
    CollectiveId Id,
    string Name,
    long Revision,
    bool CanCoordinate,
    IReadOnlyList<CollectiveMemberProjection> Members,
    CollectiveTournamentProjection? Tournament,
    CollectiveRaidRelayProjection? RaidRelay,
    CollectiveGoalProjection? Goal,
    Guid? LocalGoalSourcePublicId,
    CollectiveLocalSettingsProjection LocalSettings,
    IReadOnlyList<CollectiveAuditProjection> Audit
);

public sealed record CollectiveMemberProjection(
    int HostId,
    string Login,
    string DisplayName,
    CollectiveMembershipRole Role,
    CollectiveMembershipStatus Status,
    bool CanActForHost
);

public sealed record CollectiveTournamentProjection(
    string OwnerLogin,
    string Name,
    Guid CompetitionPublicId,
    CompetitionFormat Format,
    CompetitionStatus Status,
    int Round,
    int EntrantCount,
    int ConfirmedResultCount,
    long Revision,
    DateTime UpdatedAtUtc
);

public sealed record CollectiveRaidHandoffProjection(
    string OperationReference,
    string FromHostLogin,
    string ToHostLogin,
    int AggregateViewerCount,
    CollectiveRaidHandoffStatus Status,
    DateTime OccurredAtUtc
);

public sealed record CollectiveRaidRelayProjection(
    string Name,
    string CurrentHostLogin,
    string? NextHostLogin,
    int AggregateViewerCount,
    CollectiveWorkflowStatus Status,
    long Revision,
    IReadOnlyList<CollectiveRaidHandoffProjection> History
);

public sealed record CollectiveGoalHostProjection(
    string HostLogin,
    string HostDisplayName,
    long Total
);

public sealed record CollectiveGoalProjection(
    string Name,
    string UnitName,
    long Target,
    long Current,
    DateTime DeadlineUtc,
    CollectiveWorkflowStatus Status,
    long Revision,
    IReadOnlyList<CollectiveGoalHostProjection> HostTotals
);

public sealed record CollectiveLocalSettingsProjection(
    CollectiveLocalNotification Notification,
    long Revision
);

public sealed record CollectiveAuditProjection(
    CollectiveAuditAction Action,
    string ActingHostLogin,
    string? AffectedHostLogin,
    string ActorLogin,
    string OperationReference,
    DateTime OccurredAtUtc
);

public sealed record CollectivePublicProjection(
    CollectiveId Id,
    string Name,
    IReadOnlyList<string> ParticipatingHosts,
    CollectiveTournamentProjection? Tournament,
    CollectiveRaidRelayProjection? RaidRelay,
    CollectiveGoalProjection? Goal
);
