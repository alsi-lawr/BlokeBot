using BlokeBot.Auth.Sessions;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostConfig.Page;

public sealed class HostConfigService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostModAccessService modAccess,
    HostedChannelRuntimeStatusService runtimeStatus,
    SiteAccessService siteAccess
)
{
    public async Task<HostConfigState?> LoadAsync(
        AuthenticatedSession session,
        CancellationToken ct
    )
    {
        var login = session.Login;
        if (string.IsNullOrWhiteSpace(login))
            return null;

        var canCreateHost = await siteAccess.CanCreateHostAsync(login, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.Login == login, ct);
        if (host is null)
        {
            return new HostConfigState(
                null,
                login,
                string.IsNullOrWhiteSpace(session.DisplayName) ? login : session.DisplayName,
                session.ProfileImageUrl,
                canCreateHost,
                false,
                false,
                null,
                null,
                new HostModAccessState(true, [], [])
            );
        }

        var status = await runtimeStatus.LoadHostRuntimeSummaryAsync(host.Id, ct);
        return new HostConfigState(
            host.Id,
            host.Login,
            host.DisplayName,
            host.ProfileImageUrl,
            canCreateHost,
            true,
            host.ChannelBotAuthorizedAtUtc is not null,
            status,
            host.BotRuntimeStateChangedAtUtc,
            await modAccess.LoadAsync(host.Id, ct)
        );
    }
}
