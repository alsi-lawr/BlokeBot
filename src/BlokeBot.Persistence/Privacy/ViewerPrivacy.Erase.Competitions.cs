using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseCompetitionsAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var idKey = context.IdKey;
        var safeLoginClaims = context.SafeLoginClaims;
        var competitionIdentityClaims = context.CompetitionIdentityClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "competitions.entrants",
            await db
                .CompetitionEntrants.Where(x =>
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
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, ErasedToken), ct)
        );
        Record(
            context,
            "competitions.members",
            await db
                .CompetitionEntrantMembers.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TwitchUserId, ErasedToken)
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.DisplayName, ErasedToken)
                            .SetProperty(x => x.PrivateContact, string.Empty),
                    ct
                )
        );
        Record(
            context,
            "competitions.rewards",
            await db
                .CompetitionRewardReceipts.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TwitchUserId, ErasedToken)
                            .SetProperty(x => x.Login, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "competitions.moderation-audits",
            await db
                .CompetitionAudits.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateReason, string.Empty),
                    ct
                )
        );
        var competitionEvents = 0;
        foreach (var claim in competitionIdentityClaims)
        {
            competitionEvents += await db
                .CompetitionEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record(context, "competitions.events", competitionEvents);
        Record(
            context,
            "play-queues.entries",
            await db
                .PlayQueueEntries.Where(x =>
                    (
                        x.TwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.TwitchUserId)
                            && (
                                x.IdentityKey == idKey
                                || (
                                    (
                                        string.IsNullOrEmpty(x.IdentityKey)
                                        || x.IdentityKey.StartsWith("login:")
                                    )
                                    && safeLoginClaims.Any(claim =>
                                        claim.HostId == x.HostId
                                        && (
                                            claim.Login == x.NormalizedLogin
                                            || x.IdentityKey == "login:" + claim.Login
                                        )
                                    )
                                )
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
    }
}
