namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportMomentsAndOverlaysAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var idKey = context.IdKey;
        var safeLoginClaims = context.SafeLoginClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "moments.contributors",
            db.MomentContributors.Where(x =>
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
                                        claim.HostId == x.Candidate!.HostId
                                        && (
                                            claim.Login == x.NormalizedLogin
                                            || x.IdentityKey == "login:" + claim.Login
                                        )
                                    )
                                )
                            )
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.TwitchUserId,
                    x.NormalizedLogin,
                    x.DisplayName,
                    x.CaptureCount,
                })
        );
        await AddExportSectionAsync(
            context,
            "moments.capture-requests",
            db.MomentCaptureRequests.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.IdentityKey,
                    x.CapturedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "moments.suggestions",
            db.MomentSuggestions.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.IdentityKey,
                    x.SuggestedTitle,
                    x.SuggestedCategory,
                    x.CreatedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "moments.votes",
            db.MomentVotes.Where(x =>
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
                                        claim.HostId == x.Candidate!.HostId
                                        && (
                                            claim.Login == x.NormalizedLogin
                                            || x.IdentityKey == "login:" + claim.Login
                                        )
                                    )
                                )
                            )
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.Candidate!.HostId,
                    x.CandidateId,
                    x.TwitchUserId,
                    x.NormalizedLogin,
                    x.CreatedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "moments.moderation-audits",
            db.MomentModerationAudit.Where(x =>
                safeLoginClaims.Any(claim =>
                    claim.HostId == x.HostId && claim.Login == x.ActorLogin
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "moments.merges",
            db.MomentMerges.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.SourceCandidateId,
                    x.TargetCandidateId,
                    x.ActorLogin,
                    x.PrivateText,
                    x.MergedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "overlays.actor-events",
            db.OverlayInstanceEvents.Where(x =>
                    (
                        x.ActorUserId == userId
                        || (
                            string.IsNullOrEmpty(x.ActorUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == x.ActorLogin
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.Kind,
                    x.ActorUserId,
                    x.ActorLogin,
                    x.OccurredAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "public-chat.pins",
            db.PublicChatPinOperations.Where(x =>
                    x.PinnerTwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.Kind,
                    x.PinnerTwitchUserId,
                    x.CreatedAtUtc,
                })
        );
    }
}
