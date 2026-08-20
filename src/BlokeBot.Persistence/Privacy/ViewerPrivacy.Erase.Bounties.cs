using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseBountiesAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var quotedIdentityClaims = context.QuotedIdentityClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "bounties.pledges",
            await db
                .BountyPledges.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ContributorTwitchUserId, ErasedToken)
                            .SetProperty(x => x.ContributorLogin, ErasedToken)
                            .SetProperty(
                                x => x.State,
                                x =>
                                    x.State == BountyPledgeState.Reserved
                                        ? BountyPledgeState.Consumed
                                        : x.State
                            ),
                    ct
                )
        );
        Record(
            context,
            "bounties.rewards",
            await db
                .BountyContributorRewards.Where(x =>
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
            "bounties.moderation-audits",
            await db
                .BountyModerationAudits.Where(x =>
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
                            .SetProperty(x => x.Reason, string.Empty),
                    ct
                )
        );
        var bountyEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            bountyEvents += await db
                .BountyEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record(context, "bounties.events", bountyEvents);
    }
}
