namespace BlokeBot.Persistence.Models;

public enum MomentCandidateState
{
    [PersistedToken("ProviderPending")]
    ProviderPending,

    [PersistedToken("ClipReady")]
    ClipReady,

    [PersistedToken("MarkerReady")]
    MarkerReady,

    [PersistedToken("Failed")]
    Failed,

    [PersistedToken("Approved")]
    Approved,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("Merged")]
    Merged,
}

public enum MomentRewardPolicy
{
    [PersistedToken("None")]
    None,

    [PersistedToken("FirstRequester")]
    FirstRequester,

    [PersistedToken("AllContributors")]
    AllContributors,
}

public enum MomentEventKind
{
    [PersistedToken("Captured")]
    Captured,

    [PersistedToken("Approved")]
    Approved,

    [PersistedToken("Winner")]
    Winner,
}

public sealed class MomentHubSettings
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public int MergeWindowSeconds { get; set; } = 90;
    public bool MarkerFallbackEnabled { get; set; } = true;
    public MomentRewardPolicy RewardPolicy { get; set; }
    public string RewardAmount { get; set; } = "0";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class MomentCandidate
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int HostId { get; set; }
    public string StreamIdentity { get; set; } = string.Empty;
    public MomentCandidateState State { get; set; }
    public int? TwitchClipId { get; set; }
    public TwitchClip? TwitchClip { get; set; }
    public int? TwitchStreamMarkerId { get; set; }
    public TwitchStreamMarker? TwitchStreamMarker { get; set; }
    public string PublicTitle { get; set; } = string.Empty;
    public string PublicCategory { get; set; } = string.Empty;
    public string ProviderFailureReason { get; set; } = string.Empty;
    public string PrivateRejectionReason { get; set; } = string.Empty;
    public long? MergedIntoCandidateId { get; set; }
    public MomentCandidate? MergedIntoCandidate { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime LastCapturedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public List<MomentContributor> Contributors { get; set; } = [];
    public List<MomentCaptureRequest> CaptureRequests { get; set; } = [];
    public List<MomentSuggestion> Suggestions { get; set; } = [];
    public List<MomentVote> Votes { get; set; } = [];
}

public sealed class MomentAttachment
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long MomentCandidateId { get; set; }
    public MomentCandidate MomentCandidate { get; set; } = null!;
    public long? BountyId { get; set; }
    public Bounty? Bounty { get; set; }
    public long? CommunityDefinitionId { get; set; }
    public CommunityDefinition? CommunityDefinition { get; set; }
    public long? CompetitionMatchId { get; set; }
    public CompetitionMatch? CompetitionMatch { get; set; }
    public DateTime AttachedAtUtc { get; set; }
}

public sealed class MomentCaptureRequest
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public MomentCandidate? Candidate { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; }
}

public sealed class MomentContributor
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public MomentCandidate? Candidate { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public string? TwitchUserId { get; set; }
    public string NormalizedLogin { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int CaptureCount { get; set; }
    public DateTime FirstCapturedAtUtc { get; set; }
    public DateTime LastCapturedAtUtc { get; set; }
}

public sealed class MomentSuggestion
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public MomentCandidate? Candidate { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public string SuggestedTitle { get; set; } = string.Empty;
    public string SuggestedCategory { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class MomentVote
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public MomentCandidate? Candidate { get; set; }
    public string IdentityKey { get; set; } = string.Empty;
    public string? TwitchUserId { get; set; }
    public string NormalizedLogin { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class MomentModerationAudit
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CandidateId { get; set; }
    public MomentCandidate? Candidate { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorLogin { get; set; } = string.Empty;
    public string PrivateText { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class MomentDomainEvent
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long CandidateId { get; set; }
    public string? OperationKey { get; set; }
    public int SchemaVersion { get; set; }
    public MomentEventKind Kind { get; set; }
    public string StreamIdentity { get; set; } = string.Empty;
    public string PublicPayload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class MomentMerge
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public long SourceCandidateId { get; set; }
    public MomentCandidate? SourceCandidate { get; set; }
    public long TargetCandidateId { get; set; }
    public MomentCandidate? TargetCandidate { get; set; }
    public string ActorLogin { get; set; } = string.Empty;
    public string PrivateText { get; set; } = string.Empty;
    public DateTime MergedAtUtc { get; set; }
}

public sealed class MomentWeeklyFinalization
{
    public long Id { get; set; }
    public int HostId { get; set; }
    public DateTime WeekStartsAtUtc { get; set; }
    public long WinningCandidateId { get; set; }
    public MomentCandidate? WinningCandidate { get; set; }
    public DateTime FinalizedAtUtc { get; set; }
}
