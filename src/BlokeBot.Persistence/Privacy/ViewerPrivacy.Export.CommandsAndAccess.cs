namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task ExportCommandsAndAccessAsync(ExportContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var safeGlobalLoginClaims = context.SafeGlobalLoginClaims;
        var hostId = context.HostId;

        await AddExportSectionAsync(
            context,
            "commands.allowed-users",
            db.CustomCommandAllowedUsers.Where(x =>
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
                .Select(x => new
                {
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.Login,
                    x.DisplayName,
                })
        );
        await AddExportSectionAsync(
            context,
            "commands.usage-claims",
            db.CustomCommandInvocationClaims.Where(x =>
                    x.TwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.CustomCommandId,
                    x.TwitchUserId,
                    x.ClaimedAtUtc,
                })
        );
        await AddExportSectionAsync(
            context,
            "commands.reset-audits",
            db.CustomCommandInvocationResetAudits.Where(x =>
                (
                    x.ActorTwitchUserId == userId
                    || (
                        string.IsNullOrEmpty(x.ActorTwitchUserId)
                        && safeLoginClaims.Any(claim =>
                            claim.HostId == x.HostId && claim.Login == x.ActorLogin
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
            "configuration-imports.audits",
            db.ConfigurationImportAudits.Where(x =>
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
                .Select(x => new
                {
                    x.HostId,
                    x.OperationId,
                    x.ActorTwitchUserId,
                    x.ActorLogin,
                    x.SourceFormatVersion,
                    x.OccurredAtUtc,
                    x.SummaryJson,
                })
        );
        await AddExportSectionAsync(
            context,
            "alerts.acknowledgements",
            db.DurableAlerts.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.AcknowledgedByLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.HostId,
                    x.Title,
                    x.AcknowledgedAtUtc,
                    x.AcknowledgedByLogin,
                })
        );
        await AddExportSectionAsync(
            context,
            "access.site-entries",
            db.SiteAccessEntries.Where(x =>
                hostId == null && safeGlobalLoginClaims.Any(claim => claim.Login == x.Login)
            )
        );
        await AddExportSectionAsync(
            context,
            "access.mod-entries",
            db.HostModAccessEntries.Where(x =>
                safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
                && (hostId == null || x.HostId == hostId)
            )
        );
        await AddExportSectionAsync(
            context,
            "whispers.recipients",
            db.WhisperQuotaRecipients.Where(x =>
                    (
                        x.RecipientTwitchUserId == userId
                        || (
                            string.IsNullOrEmpty(x.RecipientTwitchUserId)
                            && safeLoginClaims.Any(claim =>
                                claim.HostId == x.WhisperQuotaBucket.HostId
                                && claim.Login == x.RecipientLogin
                            )
                        )
                    ) && (hostId == null || x.WhisperQuotaBucket.HostId == hostId)
                )
                .Select(x => new
                {
                    x.Id,
                    x.WhisperQuotaBucket.HostId,
                    x.RecipientTwitchUserId,
                    x.RecipientLogin,
                    x.FirstSentAtUtc,
                })
        );
    }
}
