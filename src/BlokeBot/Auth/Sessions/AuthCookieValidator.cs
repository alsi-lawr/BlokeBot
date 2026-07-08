using System.Security.Claims;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Auth.Sessions;

internal sealed class AuthCookieValidator(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostModAccessService modAccess
)
{
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var selectedHostId = context.Principal?.FindFirstValue(BotHostClaims.SelectedHostId);
        if (!int.TryParse(selectedHostId, out var hostId))
        {
            if (IsBotAdmin(context.Principal))
                return;

            if (
                string.Equals(
                    context.Principal?.FindFirstValue(AuthClaims.CanCreateHost),
                    "true",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            await RejectAsync(context);
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(
            context.HttpContext.RequestAborted
        );
        if (!await db.Hosts.AnyAsync(host => host.Id == hostId, context.HttpContext.RequestAborted))
        {
            await RejectAsync(context);
            return;
        }

        if (!IsModerator(context.Principal))
            return;

        var login = context.Principal?.FindFirstValue(AuthClaims.Login);
        if (
            !string.IsNullOrWhiteSpace(login)
            && await modAccess.CanModeratorAccessAsync(
                hostId,
                login,
                context.HttpContext.RequestAborted
            )
        )
        {
            return;
        }

        await RejectAsync(context);
    }

    private static bool IsModerator(ClaimsPrincipal? principal) =>
        string.Equals(
            principal?.FindFirstValue(AuthClaims.Role),
            AuthRole.Moderator,
            StringComparison.OrdinalIgnoreCase
        );

    private static bool IsBotAdmin(ClaimsPrincipal? principal) =>
        string.Equals(
            principal?.FindFirstValue(AuthClaims.IsBotAdmin),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
