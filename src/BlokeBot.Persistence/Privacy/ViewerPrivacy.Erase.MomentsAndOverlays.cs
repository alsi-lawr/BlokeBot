using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseMomentsAndOverlaysAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var idKey = context.IdKey;
        var safeLoginClaims = context.SafeLoginClaims;
        var identityContentClaims = context.IdentityContentClaims;
        var quotedIdentityClaims = context.QuotedIdentityClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "moments.contributors",
            await db
                .MomentContributors.Where(x =>
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
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "moments.capture-requests",
            await db
                .MomentCaptureRequests.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "moments.suggestions",
            await db
                .MomentSuggestions.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.Candidate!.HostId
                            && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.Candidate!.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "moments.votes",
            await db
                .MomentVotes.Where(x =>
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
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "moments.moderation-audits",
            await db
                .MomentModerationAudit.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateText, string.Empty),
                    ct
                )
        );
        Record(
            context,
            "moments.merges",
            await db
                .MomentMerges.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.ActorLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorLogin, ErasedToken)
                            .SetProperty(x => x.PrivateText, string.Empty),
                    ct
                )
        );
        var momentEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            momentEvents += await db
                .MomentEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record(context, "moments.events", momentEvents);
        Record(
            context,
            "overlays.actor-events",
            await db
                .OverlayInstanceEvents.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.ActorUserId, ErasedToken)
                            .SetProperty(x => x.ActorLogin, ErasedToken),
                    ct
                )
        );
        var overlayEventFeedItems = 0;
        foreach (var claim in identityContentClaims)
        {
            overlayEventFeedItems += await db
                .OverlayEventFeedItems.Where(x =>
                    (
                        EF.Functions.Like(x.Title, claim.Pattern, "\\")
                        || EF.Functions.Like(x.Body, claim.Pattern, "\\")
                    ) && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }
        Record(context, "overlays.event-feed", overlayEventFeedItems);

        Record(
            context,
            "public-chat.pin-operations",
            await db
                .PublicChatPinOperations.Where(x =>
                    x.PinnerTwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.PinnerTwitchUserId, (string?)null),
                    ct
                )
        );
        Record(
            context,
            "public-chat.active-pins",
            await db
                .ActivePublicChatPins.Where(x =>
                    x.PinnerTwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.PinnerTwitchUserId, ErasedToken),
                    ct
                )
        );
        var automationRuns = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            automationRuns += await db
                .AutomationFlowRuns.Where(x =>
                    EF.Functions.Like(x.ContextJson, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record(context, "automations.runs", automationRuns);
    }
}
