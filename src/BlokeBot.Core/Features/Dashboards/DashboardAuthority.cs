using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Dashboards;

internal sealed class DashboardAuthority(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IModeratorAuthorityService moderators
)
{
    internal async Task<DashboardResult<DashboardHostId>> AuthorizeAsync(
        AuthenticatedSession session,
        DashboardHostId requestedHost,
        CancellationToken ct
    )
    {
        var selected = session.State.Match<BotHostChoice?>(
            _ => null,
            value => value.Selection.Current,
            _ => null
        );
        if (
            !session.IsAuthenticated
            || session.IsBotAccount
            || string.IsNullOrWhiteSpace(session.UserId)
        )
        {
            return new DashboardResult<DashboardHostId>.Rejected(DashboardRejection.Unauthorized);
        }
        if (selected is null || selected.Id != requestedHost.Value)
        {
            return new DashboardResult<DashboardHostId>.Rejected(DashboardRejection.HostMismatch);
        }
        if (selected.Role is not (AuthRole.Streamer or AuthRole.Admin or AuthRole.Moderator))
        {
            return new DashboardResult<DashboardHostId>.Rejected(DashboardRejection.Unauthorized);
        }
        if (selected.Role == AuthRole.Moderator)
        {
            var outcome = await moderators.AuthorizeAsync(session, requestedHost.Value, ct);
            var denied = outcome.Match<DashboardRejection?>(
                _ => null,
                _ => DashboardRejection.Revoked,
                _ => DashboardRejection.HostMismatch,
                _ => DashboardRejection.AuthorityUnavailable
            );
            if (denied is { } rejection)
            {
                return new DashboardResult<DashboardHostId>.Rejected(rejection);
            }
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Hosts.AnyAsync(host => host.Id == requestedHost.Value, ct)
            ? new DashboardResult<DashboardHostId>.Succeeded(requestedHost)
            : new DashboardResult<DashboardHostId>.Rejected(DashboardRejection.Missing);
    }
}
