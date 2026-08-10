namespace BlokeBot.Persistence.Models;

public enum BountyStatus
{
    [PersistedToken("Proposed")]
    Proposed,

    [PersistedToken("Funding")]
    Funding,

    [PersistedToken("Accepted")]
    Accepted,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Failed")]
    Failed,

    [PersistedToken("Expired")]
    Expired,

    [PersistedToken("Cancelled")]
    Cancelled,
}

public enum BountyVisibility
{
    [PersistedToken("Public")]
    Public,

    [PersistedToken("Private")]
    Private,
}

public enum BountyFailurePledgePolicy
{
    [PersistedToken("Refund")]
    Refund,

    [PersistedToken("Spend")]
    Spend,
}

public enum BountyRewardDistribution
{
    [PersistedToken("Proportional")]
    Proportional,

    [PersistedToken("Equal")]
    Equal,
}

public enum BountyPledgeState
{
    [PersistedToken("Reserved")]
    Reserved,

    [PersistedToken("Refunded")]
    Refunded,

    [PersistedToken("Consumed")]
    Consumed,
}

public enum BountyAuditAction
{
    [PersistedToken("Created")]
    Created,

    [PersistedToken("FundingOpened")]
    FundingOpened,

    [PersistedToken("Accepted")]
    Accepted,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Failed")]
    Failed,

    [PersistedToken("Cancelled")]
    Cancelled,

    [PersistedToken("Rejected")]
    Rejected,

    [PersistedToken("Extended")]
    Extended,

    [PersistedToken("Expired")]
    Expired,
}

public enum BountyEventKind
{
    [PersistedToken("Created")]
    Created,

    [PersistedToken("FundingOpened")]
    FundingOpened,

    [PersistedToken("Pledged")]
    Pledged,

    [PersistedToken("FundingTargetReached")]
    FundingTargetReached,

    [PersistedToken("Accepted")]
    Accepted,

    [PersistedToken("Completed")]
    Completed,

    [PersistedToken("Failed")]
    Failed,

    [PersistedToken("Cancelled")]
    Cancelled,

    [PersistedToken("Expired")]
    Expired,

    [PersistedToken("Extended")]
    Extended,

    [PersistedToken("PledgesRefunded")]
    PledgesRefunded,

    [PersistedToken("PledgesConsumed")]
    PledgesConsumed,

    [PersistedToken("RewardsDistributed")]
    RewardsDistributed,
}

public sealed class Bounty
{
    public long Id { get; set; }

    public Guid PublicId { get; set; }

    public int HostId { get; set; }

    public Guid CreationOperationId { get; set; }

    public string CreationFingerprint { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public BountyStatus Status { get; set; }

    public BountyVisibility Visibility { get; set; }

    public BountyFailurePledgePolicy FailurePledgePolicy { get; set; }

    public BountyRewardDistribution RewardDistribution { get; set; }

    public string FundingTarget { get; set; } = "0";

    public string PledgedAmount { get; set; } = "0";

    public int ContributorCount { get; set; }

    public string CompletionReward { get; set; } = "0";

    public DateTime ExpiresAtUtc { get; set; }

    public long Revision { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public List<BountyPledge> Pledges { get; set; } = [];

    public List<BountyContributorReward> Rewards { get; set; } = [];

    public List<BountyModerationAudit> Audits { get; set; } = [];

    public List<BountyDomainEvent> Events { get; set; } = [];
}

public sealed class BountyPledge
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public long BountyId { get; set; }

    public Guid OperationId { get; set; }

    public string CommandFingerprint { get; set; } = string.Empty;

    public string ContributorTwitchUserId { get; set; } = string.Empty;

    public string ContributorLogin { get; set; } = string.Empty;

    public string Amount { get; set; } = "0";

    public BountyPledgeState State { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Bounty Bounty { get; set; } = null!;
}

public sealed class BountyContributorReward
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public long BountyId { get; set; }

    public string TwitchUserId { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string Amount { get; set; } = "0";

    public DateTime CreatedAtUtc { get; set; }

    public Bounty Bounty { get; set; } = null!;
}

public sealed class BountyModerationAudit
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public long BountyId { get; set; }

    public Guid OperationId { get; set; }

    public string CommandFingerprint { get; set; } = string.Empty;

    public BountyAuditAction Action { get; set; }

    public BountyStatus FromStatus { get; set; }

    public BountyStatus ToStatus { get; set; }

    public string ActorTwitchUserId { get; set; } = string.Empty;

    public string ActorLogin { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public long BountyRevision { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Bounty Bounty { get; set; } = null!;
}

public sealed class BountyDomainEvent
{
    public long Id { get; set; }

    public int HostId { get; set; }

    public long BountyId { get; set; }

    public Guid BountyPublicId { get; set; }

    public string? OperationKey { get; set; }

    public int SchemaVersion { get; set; }

    public BountyEventKind Kind { get; set; }

    public string PublicPayload { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public Bounty Bounty { get; set; } = null!;
}
