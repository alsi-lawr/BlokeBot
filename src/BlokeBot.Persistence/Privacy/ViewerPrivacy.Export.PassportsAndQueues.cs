namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportPassportsAndQueuesAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var idKey = context.IdKey;
        var passportIds = context.PassportIds;
        var safeLoginClaims = context.SafeLoginClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "viewer-passports.profiles",
            db.ViewerPassports.Where(x => passportIds.Contains(x.Id))
        );
        await AddExportSectionAsync(
            context,
            "viewer-passports.logins",
            db.ViewerPassportLogins.Where(x =>
                    passportIds.Contains(x.PassportId) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.HostId,
                    x.Login,
                    x.FirstSeenAtUtc,
                    x.LastSeenAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "viewer-passports.stream-attendance",
            from attendance in db.ViewerPassportStreamAttendances
            join session in db.ViewerPassportStreamSessions
                on new { attendance.HostId, Id = attendance.StreamSessionId } equals new
                {
                    session.HostId,
                    session.Id,
                }
            where
                passportIds.Contains(attendance.PassportId)
                && (hostId == null || attendance.HostId == hostId)
            select new
            {
                attendance.HostId,
                session.TwitchStreamId,
                session.StartedAtUtc,
                session.ContinuityGeneration,
                attendance.FirstSeenAtUtc,
            }
        );
        await AddExportSectionAsync(
            context,
            "play-queues.entries",
            db.PlayQueueEntries.Where(x =>
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
                                        claim.HostId == x.HostId
                                        && (
                                            claim.Login == x.NormalizedLogin
                                            || x.IdentityKey == "login:" + claim.Login
                                        )
                                    )
                                )
                            )
                        )
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.QueueId,
                    x.TwitchUserId,
                    x.NormalizedLogin,
                    x.DisplayName,
                    x.Status,
                    x.JoinedAtUtc,
                    Values = x.Values.Select(value => value.Value).ToList(),
                })
        );
        await AddExportSectionAsync(
            context,
            "play-queues.participation",
            db.PlayQueueParticipation.Where(x =>
                (
                    x.IdentityKey == idKey
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "play-queues.exclusions",
            db.PlayQueueExclusions.Where(x =>
                (
                    x.IdentityKey == idKey
                    || safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && x.IdentityKey == "login:" + claim.Login
                    )
                ) && (hostId == null || x.HostId == hostId)
            )
        );
    }
}
