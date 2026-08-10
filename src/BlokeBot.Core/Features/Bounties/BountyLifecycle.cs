using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bounties;

internal static class BountyLifecycle
{
    public static BountyStatus? Target(BountyStatus current, BountyTransitionAction action) =>
        (current, action) switch
        {
            (BountyStatus.Proposed, BountyTransitionAction.OpenFunding) => BountyStatus.Funding,
            (BountyStatus.Proposed, BountyTransitionAction.Reject) => BountyStatus.Cancelled,
            (BountyStatus.Proposed, BountyTransitionAction.Cancel) => BountyStatus.Cancelled,
            (BountyStatus.Funding, BountyTransitionAction.Accept) => BountyStatus.Accepted,
            (BountyStatus.Funding, BountyTransitionAction.Reject) => BountyStatus.Cancelled,
            (BountyStatus.Funding, BountyTransitionAction.Cancel) => BountyStatus.Cancelled,
            (BountyStatus.Funding, BountyTransitionAction.Expire) => BountyStatus.Expired,
            (BountyStatus.Accepted, BountyTransitionAction.Complete) => BountyStatus.Completed,
            (BountyStatus.Accepted, BountyTransitionAction.Fail) => BountyStatus.Failed,
            (BountyStatus.Accepted, BountyTransitionAction.Cancel) => BountyStatus.Cancelled,
            (BountyStatus.Accepted, BountyTransitionAction.Expire) => BountyStatus.Expired,
            _ => null,
        };

    public static bool CanExtend(BountyStatus status) =>
        status is BountyStatus.Funding or BountyStatus.Accepted;

    public static bool RefundsPledges(
        BountyStatus target,
        BountyFailurePledgePolicy failurePolicy
    ) =>
        target is BountyStatus.Cancelled or BountyStatus.Expired
        || (target == BountyStatus.Failed && failurePolicy == BountyFailurePledgePolicy.Refund);

    public static bool ConsumesPledges(
        BountyStatus target,
        BountyFailurePledgePolicy failurePolicy
    ) =>
        target == BountyStatus.Completed
        || (target == BountyStatus.Failed && failurePolicy == BountyFailurePledgePolicy.Spend);
}
