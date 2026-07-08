using BlokeBot.Auth.Moderation;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Auth.Hosts;

internal sealed class AuthorizedHostResolver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    SiteAccessService siteAccess,
    HostModAccessService modAccess,
    ModeratedChannelLookupService moderatedChannels
)
{
    public async Task<AuthorizedHostSet> LoadAuthorizedHostsAsync(
        WebAuthOptions options,
        string accessToken,
        string userId,
        string userLogin,
        string displayName,
        string? profileImageUrl,
        CancellationToken ct
    )
    {
        var choices = new List<BotHostChoice>();
        var canCreateHost = await siteAccess.CanCreateHostAsync(userLogin, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var selfHost = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Login == userLogin, ct);
        if (selfHost is not null)
            choices.Add(
                new BotHostChoice(
                    selfHost.Id,
                    selfHost.Login,
                    selfHost.DisplayName,
                    AuthRole.Streamer,
                    selfHost.ProfileImageUrl
                )
            );

        var moderatedLogins = await moderatedChannels.LoadModeratedLoginsAsync(
            options,
            accessToken,
            userId,
            userLogin,
            ct
        );

        if (moderatedLogins.Count == 0)
            return new AuthorizedHostSet(Sort(choices), canCreateHost);

        var configuredHosts = await db
            .Hosts.AsNoTracking()
            .Where(host => moderatedLogins.Contains(host.Login))
            .OrderBy(host => host.DisplayName)
            .Select(host => new BotHostChoice(
                host.Id,
                host.Login,
                host.DisplayName,
                AuthRole.Moderator,
                host.ProfileImageUrl
            ))
            .ToListAsync(ct);

        foreach (var host in configuredHosts)
        {
            if (await modAccess.CanModeratorAccessAsync(host.Id, userLogin, ct))
                choices.Add(host);
        }

        return new AuthorizedHostSet(Sort(choices), canCreateHost);
    }

    private static BotHostChoice[] Sort(IEnumerable<BotHostChoice> choices)
    {
        return choices
            .DistinctBy(host => host.Id)
            .OrderByDescending(host => host.Role == AuthRole.Streamer)
            .ThenBy(host => host.DisplayName)
            .ToArray();
    }
}
