using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ErasePointsAsync(ErasureContext context)
    {
        var db = context.Db;
        var safeLoginClaims = context.SafeLoginClaims;
        var bountyPledgeIds = context.BountyPledgeIds;
        var bountyRewardIds = context.BountyRewardIds;
        var communityCompletionIds = context.CommunityCompletionIds;
        var identityContentClaims = context.IdentityContentClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "guessing.votes",
            await db
                .Votes.Where(x =>
                    (hostId == null || x.GuessRound!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.GuessRound!.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "points.balances",
            await db
                .PointBalances.Where(x =>
                    (hostId == null || x.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "points.ledger.subject-rows",
            await db
                .PointLedgerEntries.Where(x =>
                    x.BountyPledgeId == null
                    && x.BountyRewardId == null
                    && x.CommunityCompletionId == null
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.Login
                    )
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.Note, string.Empty),
                    ct
                )
        );
        Record(
            context,
            "points.ledger.actor-references",
            await db
                .PointLedgerEntries.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorLogin, (string?)null)
                            .SetProperty(x => x.Note, string.Empty),
                    ct
                )
        );
        Record(
            context,
            "points.ledger.counterparty-references",
            await db
                .PointLedgerEntries.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.CounterpartyLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CounterpartyLogin, (string?)null)
                            .SetProperty(x => x.Note, string.Empty),
                    ct
                )
        );
        var pointLedgerPrivateNotes = 0;
        foreach (var claim in identityContentClaims)
        {
            pointLedgerPrivateNotes += await db
                .PointLedgerEntries.Where(x =>
                    EF.Functions.Like(x.Note, claim.Pattern, "\\")
                    && (
                        (
                            x.BountyPledgeId == null
                            && x.BountyRewardId == null
                            && x.CommunityCompletionId == null
                        )
                        || bountyPledgeIds.Contains(x.BountyPledgeId ?? 0)
                        || bountyRewardIds.Contains(x.BountyRewardId ?? 0)
                        || communityCompletionIds.Contains(x.CommunityCompletionId ?? 0)
                    )
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Note, string.Empty), ct);
        }
        Record(context, "points.ledger.private-notes", pointLedgerPrivateNotes);
        if (bountyPledgeIds.Length > 0 || bountyRewardIds.Length > 0)
        {
            Record(
                context,
                "bounties.ledger",
                await db
                    .PointLedgerEntries.Where(x =>
                        (
                            bountyPledgeIds.Contains(x.BountyPledgeId ?? 0)
                            || bountyRewardIds.Contains(x.BountyRewardId ?? 0)
                        ) && (hostId == null || x.HostId == hostId)
                    )
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(x => x.Login, ErasedToken)
                                .SetProperty(x => x.ActorLogin, (string?)null)
                                .SetProperty(x => x.Note, string.Empty),
                        ct
                    )
            );
        }
        Record(
            context,
            "points.giveaway-entries",
            await db
                .PointsGiveawayEntrants.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "points.giveaway-wins",
            await db
                .PointsGiveawayWinners.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
                )
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Login, ErasedToken), ct)
        );
    }
}
