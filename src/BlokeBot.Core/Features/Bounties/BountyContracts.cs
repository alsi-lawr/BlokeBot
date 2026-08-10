using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bounties;

public static class BountyLimits
{
    public const int MaximumTitleLength = 160;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumReasonLength = 1000;
    public const int MaximumEventReadCount = 200;
}

public sealed record BountyActor(string TwitchUserId, string Login);

public sealed record CreateBountyCommand(
    Guid OperationId,
    string Title,
    string Description,
    PointAmount FundingTarget,
    DateTime ExpiresAtUtc,
    PointAmount CompletionReward,
    BountyVisibility Visibility,
    BountyFailurePledgePolicy FailurePledgePolicy,
    BountyRewardDistribution RewardDistribution,
    BountyActor Actor,
    string Reason = ""
);

public sealed record PledgeBountyCommand(
    Guid OperationId,
    Guid BountyPublicId,
    BountyActor Contributor,
    PointAmount RequestedAmount
);

public enum BountyTransitionAction
{
    OpenFunding,
    Accept,
    Complete,
    Fail,
    Cancel,
    Reject,
    Expire,
}

public sealed record TransitionBountyCommand(
    Guid OperationId,
    Guid BountyPublicId,
    long ExpectedRevision,
    BountyTransitionAction Action,
    BountyActor Actor,
    string Reason = ""
);

public sealed record ExtendBountyCommand(
    Guid OperationId,
    Guid BountyPublicId,
    long ExpectedRevision,
    DateTime ExpiresAtUtc,
    BountyActor Actor,
    string Reason = ""
);

public sealed record BountyView(
    Guid PublicId,
    int HostId,
    string HostLogin,
    string Title,
    string Description,
    BountyStatus Status,
    BountyVisibility Visibility,
    BountyFailurePledgePolicy FailurePledgePolicy,
    BountyRewardDistribution RewardDistribution,
    PointAmount FundingTarget,
    PointAmount PledgedAmount,
    PointAmount CompletionReward,
    int ContributorCount,
    DateTime ExpiresAtUtc,
    long Revision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? ResolvedAtUtc,
    IReadOnlyList<BountyContributorView> Contributors,
    IReadOnlyList<BountyPublicHistoryView> TerminalHistory
);

public sealed record BountyContributorView(string Login, PointAmount PledgedAmount);

public sealed record BountyPublicHistoryView(
    BountyAuditAction Action,
    BountyStatus Status,
    DateTime OccurredAtUtc
);

public sealed record BountyModerationAuditView(
    BountyAuditAction Action,
    BountyStatus FromStatus,
    BountyStatus ToStatus,
    string ActorTwitchUserId,
    string ActorLogin,
    string Reason,
    long BountyRevision,
    DateTime OccurredAtUtc
);

public sealed record BountyModeratorView(
    BountyView Bounty,
    IReadOnlyList<BountyModerationAuditView> Audits
);

public sealed record BountyPledgeView(
    long Id,
    Guid BountyPublicId,
    string ContributorLogin,
    PointAmount ReservedAmount,
    BountyPledgeState State,
    DateTime CreatedAtUtc
);

public sealed record BountyEventView(
    long Id,
    int HostId,
    Guid BountyPublicId,
    int SchemaVersion,
    BountyEventKind Kind,
    string PublicPayload,
    DateTime OccurredAtUtc
);

public abstract record BountyResult<T>
{
    private BountyResult() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Rejected, TResult> rejected
    );

    public sealed record Succeeded(T Value, bool WasIdempotent = false) : BountyResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => succeeded(this);
    }

    public sealed record Rejected(BountyRejection Reason) : BountyResult<T>
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}

public abstract record BountyRejection(string Message)
{
    public sealed record FeatureDisabled()
        : BountyRejection("Bounties and points must be enabled for this channel.");

    public sealed record Invalid(string Detail) : BountyRejection(Detail);

    public sealed record NotFound() : BountyRejection("Bounty not found.");

    public sealed record Conflict(string Detail) : BountyRejection(Detail);

    public sealed record StaleRevision(long CurrentRevision)
        : BountyRejection($"The bounty changed at revision {CurrentRevision}.");

    public sealed record InvalidTransition(BountyStatus Status, BountyTransitionAction Action)
        : BountyRejection($"A {Status} bounty cannot be changed with {Action}.");

    public sealed record FundingClosed() : BountyRejection("This bounty is not accepting pledges.");

    public sealed record InsufficientPoints(PointAmount Available, PointAmount Requested)
        : BountyRejection(
            $"Only {Available.ToDisplayString()} points are available for this pledge of {Requested.ToDisplayString()}."
        );

    public sealed record PointCapExceeded(string Login)
        : BountyRejection($"The point balance for @{Login} cannot receive this amount.");
}
