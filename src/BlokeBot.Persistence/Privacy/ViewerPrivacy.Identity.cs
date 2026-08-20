using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static Task<bool> IsAmbiguousLoginAsync(
        BlokeBotDbContext db,
        string? login,
        int? hostId,
        CancellationToken cancellationToken
    ) =>
        login is null
            ? Task.FromResult(false)
            : db
                .ViewerPassportAmbiguousLogins.AsNoTracking()
                .AnyAsync(
                    value => value.Login == login && (hostId == null || value.HostId == hostId),
                    cancellationToken
                );

    private static async Task<PrivacyIdentityScope> ResolveIdentityScopeAsync(
        BlokeBotDbContext db,
        PrivacySubject subject,
        int? hostId,
        CancellationToken cancellationToken
    )
    {
        var userId = subject.TwitchUserId ?? UnmatchableValue;
        var passports = db.ViewerPassports.Where(passport =>
            hostId == null || passport.HostId == hostId
        );
        long[] passportIds;
        var globalAliasOwnerUserId = subject.TwitchUserId ?? UnmatchableValue;
        if (subject.TwitchUserId is not null)
        {
            passportIds = await passports
                .Where(passport => passport.TwitchUserId == userId)
                .Select(passport => passport.Id)
                .ToArrayAsync(cancellationToken);
        }
        else if (
            subject.Login is null
            || await IsAmbiguousLoginAsync(db, subject.Login, hostId, cancellationToken)
        )
        {
            passportIds = [];
        }
        else
        {
            var matches = await passports
                .Where(passport =>
                    passport.Login == subject.Login
                    || db.ViewerPassportLogins.Any(alias =>
                        alias.PassportId == passport.Id && alias.Login == subject.Login
                    )
                )
                .Select(passport => new PassportOwner(passport.Id, passport.TwitchUserId))
                .ToArrayAsync(cancellationToken);
            var stableOwners = matches
                .Select(match => match.TwitchUserId)
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            passportIds =
                matches.Length > 0
                && stableOwners.Length == 1
                && matches.All(match => match.TwitchUserId == stableOwners[0])
                    ? matches.Select(match => match.PassportId).ToArray()
                    : [];
            if (passportIds.Length > 0)
            {
                globalAliasOwnerUserId = stableOwners[0];
            }
        }
        return new(
            userId,
            subject.TwitchUserId is null ? UnmatchableValue : subject.IdIdentityKey,
            passportIds,
            globalAliasOwnerUserId
        );
    }

    private static IQueryable<ViewerPassportLogin> SafeLoginClaims(
        BlokeBotDbContext db,
        IReadOnlyCollection<long> passportIds
    ) =>
        db.ViewerPassportLogins.Where(alias =>
            passportIds.Contains(alias.PassportId)
            && !db.ViewerPassportAmbiguousLogins.Any(ambiguous =>
                ambiguous.HostId == alias.HostId && ambiguous.Login == alias.Login
            )
        );

    private static IQueryable<ViewerPassportLogin> SafeGlobalLoginClaims(
        BlokeBotDbContext db,
        IQueryable<ViewerPassportLogin> safeLoginClaims,
        string globalAliasOwnerUserId
    ) =>
        safeLoginClaims.Where(alias =>
            globalAliasOwnerUserId != UnmatchableValue
            && !db.ViewerPassportAmbiguousLogins.Any(ambiguous => ambiguous.Login == alias.Login)
            && !db.ViewerPassports.Any(passport =>
                (
                    passport.Login == alias.Login
                    || db.ViewerPassportLogins.Any(claim =>
                        claim.PassportId == passport.Id && claim.Login == alias.Login
                    )
                )
                && (
                    string.IsNullOrEmpty(passport.TwitchUserId)
                    || passport.TwitchUserId != globalAliasOwnerUserId
                )
            )
        );

    private static async Task<LinkedLedgerClaims> ResolveLinkedLedgerClaimsAsync(
        BlokeBotDbContext db,
        string userId,
        IQueryable<ViewerPassportLogin> safeLoginClaims,
        int? hostId,
        CancellationToken cancellationToken
    )
    {
        var bountyPledgeIds = await db
            .BountyPledges.Where(value =>
                (
                    value.ContributorTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(value.ContributorTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == value.HostId && claim.Login == value.ContributorLogin
                        )
                    )
                ) && (hostId == null || value.HostId == hostId)
            )
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var bountyRewardIds = await db
            .BountyContributorRewards.Where(value =>
                (
                    value.TwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(value.TwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == value.HostId && claim.Login == value.Login
                        )
                    )
                ) && (hostId == null || value.HostId == hostId)
            )
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var communityCompletionIds = await db
            .CommunityCompletions.Where(value =>
                (
                    value.ViewerTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(value.ViewerTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == value.HostId && claim.Login == value.ViewerLogin
                        )
                    )
                ) && (hostId == null || value.HostId == hostId)
            )
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        return new(bountyPledgeIds, bountyRewardIds, communityCompletionIds);
    }

    private static PrivacyTextClaim[] IdentityTextClaims(
        string? twitchUserId,
        int? hostId,
        IReadOnlyCollection<SafeLoginClaim> safeLoginClaims,
        Func<string, string> pattern
    ) =>
        safeLoginClaims
            .Select(value => new PrivacyTextClaim(value.HostId, pattern(value.Login)))
            .Concat(
                twitchUserId is null ? [] : [new PrivacyTextClaim(hostId, pattern(twitchUserId))]
            )
            .Distinct()
            .ToArray();

    private sealed record PrivacyIdentityScope(
        string UserId,
        string IdIdentityKey,
        long[] PassportIds,
        string GlobalAliasOwnerUserId
    );

    private sealed record PassportOwner(long PassportId, string TwitchUserId);

    private sealed record LinkedLedgerClaims(
        long[] BountyPledgeIds,
        long[] BountyRewardIds,
        long[] CommunityCompletionIds
    );

    private sealed record SafeLoginClaim(int HostId, string Login);

    private sealed record PrivacyTextClaim(int? HostId, string Pattern);

    private static string LikeContainsPattern(string value) =>
        $"%{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
}
