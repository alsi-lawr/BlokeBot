using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ErasePlayQueuesAsync(ErasureContext context)
    {
        var db = context.Db;
        var idKey = context.IdKey;
        var safeLoginClaims = context.SafeLoginClaims;
        var quotedIdentityClaims = context.QuotedIdentityClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "play-queues.participation",
            await db
                .PlayQueueParticipation.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "play-queues.exclusions",
            await db
                .PlayQueueExclusions.Where(x =>
                    (
                        x.IdentityKey == idKey
                        || safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        var playQueueEvents = 0;
        foreach (var claim in quotedIdentityClaims)
        {
            playQueueEvents += await db
                .PlayQueueEvents.Where(x =>
                    EF.Functions.Like(x.PublicPayload, claim.Pattern, "\\")
                    && (claim.HostId == null || x.HostId == claim.HostId)
                )
                .ExecuteDeleteAsync(ct);
        }

        Record(context, "play-queues.events", playQueueEvents);
    }
}
