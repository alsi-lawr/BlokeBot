using System.Diagnostics;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Hosts;

internal sealed class AuthorizedHostSelectionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    SiteAccessService siteAccess,
    HostModAccessService modAccess,
    ModeratedChannelLookupService moderatedChannels
)
{
    public async Task<AuthorizedHostSet> LoadAuthorizedHostsAsync(
        string accessToken,
        string userId,
        string userLogin,
        CancellationToken ct
    )
    {
        var choices = new List<BotHostChoice>();
        var canCreateHost = await siteAccess.CanCreateHostAsync(userLogin, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var selfHost = await LoadSelfHostChoice(db, userLogin).ExecuteAsync(ct);
        selfHost.Match(
            host =>
                host.Match(
                    value =>
                    {
                        choices.Add(value);
                        return true;
                    },
                    () => false
                ),
            _ => throw new UnreachableException()
        );

        choices.AddRange(
            await LoadModeratedHostChoicesAsync(db, accessToken, userId, userLogin, ct)
        );

        return new AuthorizedHostSet(Sort(choices), canCreateHost);
    }

    private static IO<Option<BotHostChoice>, Never> LoadSelfHostChoice(
        BlokeBotDbContext db,
        string userLogin
    )
    {
        return IO<Option<BotHostChoice>, Never>.Create(async ct =>
        {
            var selfHost = await db
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Login == userLogin, ct);
            return Result<Option<BotHostChoice>, Never>.Success(
                Option<BotHostChoice>.FromNullable(
                    selfHost is null
                        ? null
                        : new BotHostChoice(
                            selfHost.Id,
                            selfHost.Login,
                            selfHost.DisplayName,
                            AuthRole.Streamer,
                            selfHost.ProfileImageUrl
                        )
                )
            );
        });
    }

    private async Task<IReadOnlyList<BotHostChoice>> LoadModeratedHostChoicesAsync(
        BlokeBotDbContext db,
        string accessToken,
        string userId,
        string userLogin,
        CancellationToken ct
    )
    {
        var moderatedLogins = await moderatedChannels.LoadModeratedLoginsAsync(
            accessToken,
            userId,
            userLogin,
            ct
        );

        if (moderatedLogins.Count == 0)
        {
            return [];
        }

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

        var choices = new List<BotHostChoice>();
        foreach (var host in configuredHosts)
        {
            if (await modAccess.CanModeratorAccessAsync(host.Id, userLogin, ct))
            {
                choices.Add(host);
            }
        }

        return choices;
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
