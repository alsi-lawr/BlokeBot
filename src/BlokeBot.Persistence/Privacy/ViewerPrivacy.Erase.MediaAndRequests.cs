using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseMediaAndRequestsAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var quotedIdentityClaims = context.QuotedIdentityClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "shoutouts.history",
            await db
                .ShoutoutHistory.Where(x =>
                    (
                        x.SourceTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.SourceTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.SourceLogin
                            )
                        )
                        || x.TargetTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.TargetTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.TargetLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "shoutouts.cooldowns",
            await db
                .ShoutoutCooldowns.Where(x =>
                    (
                        x.TargetTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.TargetTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.TargetLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "shoutouts.raid-outcomes",
            await db
                .AutomaticRaidShoutoutOutcomes.Where(x =>
                    (
                        x.SourceTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.SourceTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.SourceLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "channel-points.redemptions",
            await db
                .TwitchRewardRedemptions.Where(x =>
                    (
                        x.UserId == userId
                        || (
                            string.IsNullOrEmpty(x.UserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.UserLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "clips.creator-references",
            await db
                .TwitchClips.Where(x =>
                    (
                        x.CreatorTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.CreatorTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.CreatorLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CreatorTwitchUserId, (string?)null)
                            .SetProperty(x => x.CreatorLogin, (string?)null),
                    ct
                )
        );

        var votedSubmissionIds = await db
            .RequestSubmissionVotes.Where(x =>
                safeLoginClaims.Any(claim =>
                    claim.HostId == x.Submission!.HostId && claim.Login == x.VoterLogin
                ) && (hostId == null || x.Submission!.HostId == hostId)
            )
            .Select(x => x.SubmissionId)
            .Distinct()
            .ToListAsync(ct);
        Record(
            context,
            "request-boards.votes",
            await db
                .RequestSubmissionVotes.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.Submission!.HostId && claim.Login == x.VoterLogin
                    ) && (hostId == null || x.Submission!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        if (votedSubmissionIds.Count > 0)
        {
            _ = await db
                .RequestSubmissions.Where(x => votedSubmissionIds.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.VoteCount, x => x.Votes.Count),
                    ct
                );
        }

        Record(
            context,
            "request-boards.submissions",
            await db
                .RequestSubmissions.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.SubmitterLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        var requestBoardEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            requestBoardEvents += await db
                .RequestBoardEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record(context, "request-boards.events", requestBoardEvents);
    }
}
