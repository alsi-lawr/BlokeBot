using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task<ViewerErasureReport> EraseInSnapshotAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken ct
    )
    {
        var changed = new Dictionary<string, int>(StringComparer.Ordinal);
        var scope = await ResolveIdentityScopeAsync(db, subject, hostId, ct);
        var userId = scope.UserId;
        var idKey = scope.IdIdentityKey;
        var passportIds = scope.PassportIds;
        var safeLoginClaims = SafeLoginClaims(db, passportIds);
        var safeGlobalLoginClaims = SafeGlobalLoginClaims(
            db,
            safeLoginClaims,
            scope.GlobalAliasOwnerUserId
        );
        var linkedLedgerClaims = await ResolveLinkedLedgerClaimsAsync(
            db,
            userId,
            safeLoginClaims,
            hostId,
            ct
        );
        var safeLoginClaimValues = await safeLoginClaims
            .Select(value => new SafeLoginClaim(value.HostId, value.Login))
            .Distinct()
            .ToArrayAsync(ct);
        var quotedIdentityClaims = IdentityTextClaims(
            subject.TwitchUserId,
            hostId,
            safeLoginClaimValues,
            static value => LikeContainsPattern($"\"{value}\"")
        );
        var competitionLoginClaims = await db
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
            .Select(x => new SafeLoginClaim(x.HostId, x.Login))
            .Distinct()
            .ToArrayAsync(ct);
        var competitionIdentityClaims = IdentityTextClaims(
            subject.TwitchUserId,
            hostId,
            competitionLoginClaims,
            static value => LikeContainsPattern($"\"{value}\"")
        );

        var bountyPledgeIds = linkedLedgerClaims.BountyPledgeIds;
        var bountyRewardIds = linkedLedgerClaims.BountyRewardIds;
        var communityCompletionIds = linkedLedgerClaims.CommunityCompletionIds;
        var bingoParticipants = await db
            .BingoParticipants.Where(x =>
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
            .Where(x => x.CardId != null)
            .Select(x => x.CardId!.Value)
            .ToListAsync(ct);
        var uniqueBingoCards = await db
            .BingoCards.Where(x =>
                bingoParticipants.Contains(x.Id)
                && x.Game!.Mode == BingoGameMode.UniquePerViewer
                && (hostId == null || x.HostId == hostId)
            )
            .Select(x => x.Id)
            .ToListAsync(ct);
        var uniqueBingoCardIdsToErase = uniqueBingoCards.ToArray();
        var identityContentClaims = IdentityTextClaims(
            subject.TwitchUserId,
            hostId,
            safeLoginClaimValues,
            LikeContainsPattern
        );
        var context = new ErasureContext(
            db,
            changed,
            userId,
            idKey,
            passportIds,
            safeLoginClaims,
            safeGlobalLoginClaims,
            bountyPledgeIds,
            bountyRewardIds,
            communityCompletionIds,
            uniqueBingoCardIdsToErase,
            identityContentClaims,
            quotedIdentityClaims,
            competitionIdentityClaims,
            hostId,
            ct
        );

        await ErasePointsAsync(context);
        await EraseCommandsAndAccessAsync(context);
        await EraseMediaAndRequestsAsync(context);
        await EraseBountiesAsync(context);
        await EraseBingoAsync(context);
        await EraseCommunityAsync(context);
        await EraseCompetitionsAsync(context);
        await ErasePlayQueuesAsync(context);
        await EraseMomentsAndOverlaysAsync(context);
        await ErasePassportsAsync(context);

        return new ViewerErasureReport(changed);
    }

    private static void Record(ErasureContext context, string section, int rows)
    {
        if (rows > 0)
        {
            context.Changed[section] = rows;
        }
    }

    private sealed record ErasureContext(
        BlokeBotDbContext Db,
        Dictionary<string, int> Changed,
        string UserId,
        string IdKey,
        long[] PassportIds,
        IQueryable<ViewerPassportLogin> SafeLoginClaims,
        IQueryable<ViewerPassportLogin> SafeGlobalLoginClaims,
        long[] BountyPledgeIds,
        long[] BountyRewardIds,
        long[] CommunityCompletionIds,
        long[] UniqueBingoCardIdsToErase,
        PrivacyTextClaim[] IdentityContentClaims,
        PrivacyTextClaim[] QuotedIdentityClaims,
        PrivacyTextClaim[] CompetitionIdentityClaims,
        int? HostId,
        CancellationToken CancellationToken
    );
}
