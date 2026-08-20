using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseBingoAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var uniqueBingoCardIdsToErase = context.UniqueBingoCardIdsToErase;
        var identityContentClaims = context.IdentityContentClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "bingo.unique-cards",
            await db
                .BingoCards.Where(x => uniqueBingoCardIdsToErase.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AssignmentName, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "bingo.participants",
            await db
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TwitchUserId, x => "erased:" + x.Id)
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.DisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "bingo.evidence",
            await db
                .BingoEvidence.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ParticipantTwitchUserId, (string?)null)
                            .SetProperty(x => x.ParticipantLogin, (string?)null)
                            .SetProperty(x => x.ParticipantDisplayName, (string?)null)
                            .SetProperty(x => x.Summary, "Bingo event recorded"),
                    ct
                )
        );
        Record(
            context,
            "bingo.win-recipients",
            await db
                .BingoWinRecipients.Where(x =>
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
                            .SetProperty(x => x.TwitchUserId, x => "erased:" + x.Id)
                            .SetProperty(x => x.Login, ErasedToken)
                            .SetProperty(x => x.DisplayName, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "bingo.moderation-audits",
            await db
                .BingoModerationAudit.Where(x =>
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
        Record(
            context,
            "bingo.template-revisions",
            await db
                .BingoTemplateRevisions.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CreatedByTwitchUserId, ErasedToken)
                            .SetProperty(x => x.CreatedByLogin, ErasedToken),
                    ct
                )
        );
        var bingoEvidenceText = 0;
        var bingoAuditText = 0;
        var bingoEvents = 0;
        var bingoOverlayItems = 0;
        foreach (var claim in identityContentClaims)
        {
            bingoEvidenceText += await db
                .BingoEvidence.Where(x =>
                    string.IsNullOrEmpty(x.ParticipantTwitchUserId)
                    && EF.Functions.Like(x.Summary, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Summary, "Bingo event recorded"),
                    ct
                );
            bingoAuditText += await db
                .BingoModerationAudit.Where(x =>
                    string.IsNullOrEmpty(x.ActorTwitchUserId)
                    && EF.Functions.Like(x.PrivateNote, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.PrivateNote, string.Empty),
                    ct
                );
            bingoEvents += await db
                .BingoEvents.Where(x =>
                    (
                        EF.Functions.Like(x.OperationKey, claim.Pattern, "\\")
                        || EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    ) && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
            bingoOverlayItems += await db
                .OverlayEventFeedItems.Where(x =>
                    (
                        EF.Functions.Like(x.SourceKey, claim.Pattern, "\\")
                        || EF.Functions.Like(x.Title, claim.Pattern, "\\")
                        || EF.Functions.Like(x.Body, claim.Pattern, "\\")
                    ) && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record(context, "bingo.evidence-text", bingoEvidenceText);
        Record(context, "bingo.moderation-audit-text", bingoAuditText);
        Record(context, "bingo.events", bingoEvents);
        Record(context, "bingo.overlay-items", bingoOverlayItems);
    }
}
