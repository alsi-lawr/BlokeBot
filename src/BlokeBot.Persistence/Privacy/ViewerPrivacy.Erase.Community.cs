using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseCommunityAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var communityCompletionIds = context.CommunityCompletionIds;
        var quotedIdentityClaims = context.QuotedIdentityClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        if (communityCompletionIds.Length > 0)
        {
            Record(
                context,
                "community.points-ledger",
                await db
                    .PointLedgerEntries.Where(x =>
                        communityCompletionIds.Contains(x.CommunityCompletionId ?? 0)
                        && (hostId == null || x.HostId == hostId)
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
            "community.equipped-rewards",
            await db
                .CommunityEquippedRewards.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.ViewerTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "community.reward-unlocks",
            await db
                .CommunityRewardUnlocks.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.ViewerTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "community.progress",
            await db
                .CommunityProgress.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.ViewerTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "community.completions",
            await db
                .CommunityCompletions.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.ViewerTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.SubjectKey, x => "erased:" + x.Id)
                            .SetProperty(x => x.ViewerTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ViewerLogin, ErasedToken)
                            .SetProperty(x => x.ViewerDisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "community.standings",
            await db
                .CommunitySeasonStandings.Where(x =>
                    (
                        x.ViewerTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.ViewerTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.ViewerLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ViewerTwitchUserId, x => "erased:" + x.Id)
                            .SetProperty(x => x.ViewerLogin, ErasedToken)
                            .SetProperty(x => x.ViewerDisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "community.moderation-audits",
            await db
                .CommunityAudits.Where(x =>
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
                            .SetProperty(x => x.PrivateNote, string.Empty),
                    ct
                )
        );
        var communityEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            communityEvents += await db
                .CommunityEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record(context, "community.events", communityEvents);
    }
}
