namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportAccountsAndPointsAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var linkedLedgerClaims = context.LinkedLedgerClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "hosts.channels",
            db.Hosts.Where(x => x.TwitchUserId == userId)
                .Select(x => new
                {
                    x.Id,
                    x.TwitchUserId,
                    x.Login,
                    x.DisplayName,
                    x.CreatedAtUtc,
                    Note = "Hosted channel record; erased by removing the channel, not by viewer erasure.",
                })
        );
        await AddExportSectionAsync(
            context,
            "guessing.votes",
            db.Votes.Where(x =>
                    (hostId == null || x.GuessRound!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.GuessRound!.HostId && claim.Login == x.Login
                    )
                )
                .Select(x => new
                {
                    x.Id,
                    x.GuessRound!.HostId,
                    x.GuessRoundId,
                    x.Login,
                    x.GuessName,
                    x.GuessedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "points.balances",
            db.PointBalances.Where(x =>
                (hostId == null || x.HostId == hostId)
                && safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
            )
        );
        await AddExportSectionAsync(
            context,
            "points.ledger",
            db.PointLedgerEntries.Where(x =>
                (
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId
                        && (claim.Login == x.ActorLogin || claim.Login == x.CounterpartyLogin)
                    )
                    || (
                        x.BountyPledgeId == null
                        && x.BountyRewardId == null
                        && x.CommunityCompletionId == null
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    )
                    || linkedLedgerClaims.BountyPledgeIds.Contains(x.BountyPledgeId ?? 0)
                    || linkedLedgerClaims.BountyRewardIds.Contains(x.BountyRewardId ?? 0)
                    || linkedLedgerClaims.CommunityCompletionIds.Contains(
                        x.CommunityCompletionId ?? 0
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "points.giveaway-entries",
            db.PointsGiveawayEntrants.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
                )
                .Select(x => new
                {
                    x.Id,
                    x.Giveaway!.HostId,
                    x.GiveawayId,
                    x.Login,
                    x.JoinedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "points.giveaway-wins",
            db.PointsGiveawayWinners.Where(x =>
                    (hostId == null || x.Giveaway!.HostId == hostId)
                    && safeLoginClaims.Any(claim =>
                        claim.HostId == x.Giveaway!.HostId && claim.Login == x.Login
                    )
                )
                .Select(x => new
                {
                    x.Id,
                    x.Giveaway!.HostId,
                    x.GiveawayId,
                    x.Login,
                    x.Payout,
                })
        );
    }
}
