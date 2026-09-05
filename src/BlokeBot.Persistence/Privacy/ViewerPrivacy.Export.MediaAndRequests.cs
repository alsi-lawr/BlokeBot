namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportMediaAndRequestsAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "shoutouts.history",
            db.ShoutoutHistory.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "shoutouts.cooldowns",
            db.ShoutoutCooldowns.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "shoutouts.raid-outcomes",
            db.AutomaticRaidShoutoutOutcomes.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "channel-points.redemptions",
            db.TwitchRewardRedemptions.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "clips.created",
            db.TwitchClips.Where(x =>
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
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.CreatorTwitchUserId,
                    x.CreatorLogin,
                    x.FinalUrl,
                    x.RequestedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "request-boards.submissions",
            db.RequestSubmissions.Where(x =>
                    (
                        x.SubmitterTwitchUserId == userId
                        || (
                            x.SubmitterTwitchUserId == null
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.SubmitterLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.BoardId,
                    x.SubmitterLogin,
                    x.Title,
                    x.NormalizedUrl,
                    x.Status,
                    x.PublicNote,
                    x.CreatedAtUtc,
                    Values = x.Values.Select(value => value.Value).ToList(),
                })
        );
        await AddExportSectionAsync(
            context,
            "request-boards.votes",
            db.RequestSubmissionVotes.Where(x =>
                    (
                        x.VoterTwitchUserId == userId
                        || (
                            x.VoterTwitchUserId == null
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.Submission!.HostId && claim.Login == x.VoterLogin
                            )
                        )
                    ) && (hostId == null || x.Submission!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Submission!.HostId,
                    x.SubmissionId,
                    x.VoterLogin,
                    x.CreatedAtUtc,
                })
        );
    }
}
