using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Privacy;

public static partial class ViewerPrivacyService
{
    private static async Task EraseCommandsAndAccessAsync(ErasureContext context)
    {
        var db = context.Db;
        var userId = context.UserId;
        var safeLoginClaims = context.SafeLoginClaims;
        var safeGlobalLoginClaims = context.SafeGlobalLoginClaims;
        var hostId = context.HostId;
        var ct = context.CancellationToken;

        Record(
            context,
            "commands.allowed-users",
            await db
                .CustomCommandAllowedUsers.Where(x =>
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
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "commands.usage-claims",
            await db
                .CustomCommandInvocationClaims.Where(x =>
                    x.TwitchUserId == userId && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "commands.reset-audits.actor",
            await db
                .CustomCommandInvocationResetAudits.Where(x =>
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
                            .SetProperty(x => x.ActorLogin, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "configuration-imports.audits.actor",
            await db
                .ConfigurationImportAudits.Where(x =>
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
                            .SetProperty(x => x.ActorLogin, ErasedToken),
                    ct
                )
        );
        Record(
            context,
            "commands.reset-audits.target",
            await db
                .CustomCommandInvocationResetAudits.Where(x =>
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
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.TargetTwitchUserId, (string?)null)
                            .SetProperty(x => x.TargetLogin, (string?)null),
                    ct
                )
        );
        Record(
            context,
            "alerts.acknowledgements",
            await db
                .DurableAlerts.Where(x =>
                    safeLoginClaims.Any(claim =>
                        claim.HostId == x.HostId && claim.Login == x.AcknowledgedByLogin
                    ) && (hostId == null || x.HostId == hostId)
                )
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.AcknowledgedByLogin, (string?)null),
                    ct
                )
        );
        if (hostId is null)
        {
            Record(
                context,
                "access.site-entries",
                await db
                    .SiteAccessEntries.Where(x =>
                        safeGlobalLoginClaims.Any(claim => claim.Login == x.Login)
                    )
                    .ExecuteDeleteAsync(ct)
            );
        }

        Record(
            context,
            "access.mod-entries",
            await db
                .HostModAccessEntries.Where(x =>
                    safeLoginClaims.Any(claim => claim.HostId == x.HostId && claim.Login == x.Login)
                    && (hostId == null || x.HostId == hostId)
                )
                .ExecuteDeleteAsync(ct)
        );
        Record(
            context,
            "whispers.recipients",
            await db
                .WhisperQuotaRecipients.Where(x =>
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
                .ExecuteDeleteAsync(ct)
        );
    }
}
