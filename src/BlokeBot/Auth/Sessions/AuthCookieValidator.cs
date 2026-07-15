using System.Security.Claims;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Auth.Sessions;

internal sealed class AuthCookieValidator(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostModAccessService modAccess,
    SiteAccessService siteAccess,
    BotAdminService admins,
    AuthSessionService authSession
)
{
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var session = AuthenticatedSession.FromPrincipal(context.Principal);
        if (!session.IsAuthenticated || string.IsNullOrWhiteSpace(session.Login))
        {
            await RejectAsync(context);
            return;
        }

        var isConfiguredBotAccount =
            session.IsBotAccount && authSession.IsConfiguredBotAccount(session.Login);
        var isBotAdmin = isConfiguredBotAccount || admins.IsAdmin(session.Login);
        if (session.IsBotAccount != isConfiguredBotAccount || session.IsBotAdmin != isBotAdmin)
        {
            await RejectAsync(context);
            return;
        }

        await session.State.Match(
            _ => ValidateNoSelectionAsync(),
            selected => ValidateSelectionAsync(selected.Selection.Current),
            _ => RejectAsync(context)
        );

        async Task ValidateNoSelectionAsync()
        {
            if (
                isBotAdmin
                || await siteAccess.CanCreateHostAsync(
                    session.Login,
                    context.HttpContext.RequestAborted
                )
            )
            {
                return;
            }

            await RejectAsync(context);
        }

        async Task ValidateSelectionAsync(BotHostChoice currentHost)
        {
            await using var db = await dbFactory.CreateDbContextAsync(
                context.HttpContext.RequestAborted
            );
            var persistedHost = await db
                .Hosts.AsNoTracking()
                .Where(host => host.Id == currentHost.Id)
                .Select(host => new { host.Login })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (persistedHost is null)
            {
                await RejectAsync(context);
                return;
            }

            if (
                session.CurrentHostRoleIs(AuthRole.Streamer)
                && !string.Equals(
                    persistedHost.Login,
                    session.Login,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                await RejectAsync(context);
                return;
            }

            if (session.CurrentHostRoleIs(AuthRole.Admin) && !isBotAdmin)
            {
                await RejectAsync(context);
                return;
            }

            if (!session.CurrentHostRoleIs(AuthRole.Moderator))
            {
                return;
            }

            if (
                await modAccess.CanModeratorAccessAsync(
                    currentHost.Id,
                    session.Login,
                    context.HttpContext.RequestAborted
                )
            )
            {
                return;
            }

            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
