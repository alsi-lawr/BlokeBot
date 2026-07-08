using System.Security.Claims;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Web;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;

namespace BlokeBot.Features.HostConfig.Page;

internal static class HostConfigEndpoints
{
    public static void MapHostConfigEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/host/create",
                async (
                    HttpContext context,
                    string? returnUrl,
                    BotHostProvisioningService provisioning,
                    AuthSessionService session,
                    SiteAccessService siteAccess,
                    CancellationToken ct
                ) =>
                {
                    var login = session.Login(context.User);
                    if (string.IsNullOrWhiteSpace(login))
                        return Results.Redirect("/auth/login");

                    if (!await siteAccess.CanCreateHostAsync(login, ct))
                        return Results.Forbid();

                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var displayName = context.User.Identity?.Name ?? login;
                    var profileImageUrl = context.User.FindFirstValue(AuthClaims.ProfileImageUrl);
                    var hostId = await provisioning.EnsureHostAsync(
                        login,
                        userId,
                        displayName,
                        profileImageUrl,
                        ct
                    );
                    var host = new BotHostChoice(
                        hostId,
                        login,
                        displayName,
                        AuthRole.Streamer,
                        profileImageUrl
                    );
                    var current = BotHostSelectionAccessor.FromPrincipal(context.User);
                    var available =
                        current?.Available.Where(x => x.Id != host.Id).Append(host).ToArray()
                        ?? [host];

                    await session.SignInHostAsync(
                        context,
                        available,
                        host,
                        session.IsBotAdmin(context.User),
                        adminEditingLogin: null
                    );

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/host"));
                }
            )
            .RequireAuthorization();
    }
}
