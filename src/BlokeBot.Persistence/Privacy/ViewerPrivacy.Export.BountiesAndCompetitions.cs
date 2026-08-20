namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportBountiesAndCompetitionsAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "bounties.pledges",
            db.BountyPledges.Where(x =>
                (
                    x.ContributorTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.ContributorTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ContributorLogin
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "bounties.rewards",
            db.BountyContributorRewards.Where(x =>
                (
                    x.TwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.TwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "bounties.moderation-audits",
            db.BountyModerationAudits.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.ActorTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "competitions.entrants",
            db.CompetitionEntrants.Where(x =>
                    x.Members.Any(member =>
                        member.TwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(member.TwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == member.HostId && claim.Login == member.Login
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.CompetitionId,
                    x.Name,
                    x.SeedRank,
                    x.RegisteredAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "competitions.members",
            db.CompetitionEntrantMembers.Where(x =>
                (
                    x.TwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.TwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "competitions.rewards",
            db.CompetitionRewardReceipts.Where(x =>
                (
                    x.TwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.TwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.Login
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "competitions.moderation-audits",
            db.CompetitionAudits.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.ActorTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
    }
}
