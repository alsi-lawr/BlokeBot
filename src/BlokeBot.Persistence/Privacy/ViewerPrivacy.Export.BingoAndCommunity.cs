using BlokeBot.Persistence.Models;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportBingoAndCommunityAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "bingo.participants",
            db.BingoParticipants.Where(x =>
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
            "bingo.unique-cards",
            db.BingoCards.Where(x =>
                    x.Game!.Mode == BingoGameMode.UniquePerViewer
                    && x.Participants.Any(participant =>
                        participant.TwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(participant.TwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.HostId && claim.Login == participant.Login
                            )
                        )
                    )
                    && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.GameId,
                    x.AssignmentName,
                    x.IssuedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "bingo.evidence",
            db.BingoEvidence.Where(x =>
                (
                    x.ParticipantTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.ParticipantTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ParticipantLogin
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "bingo.win-recipients",
            db.BingoWinRecipients.Where(x =>
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
            "bingo.moderation-audits",
            db.BingoModerationAudit.Where(x =>
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
            "bingo.template-revisions",
            db.BingoTemplateRevisions.Where(x =>
                (
                    x.CreatedByTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.CreatedByTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.CreatedByLogin
                        )
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "community.progress",
            db.CommunityProgress.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "community.completions",
            db.CommunityCompletions.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "community.reward-unlocks",
            db.CommunityRewardUnlocks.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "community.equipped-rewards",
            db.CommunityEquippedRewards.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "community.standings",
            db.CommunitySeasonStandings.Where(x =>
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
        );
        await AddExportSectionAsync(
            context,
            "community.moderation-audits",
            db.CommunityAudits.Where(x =>
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
