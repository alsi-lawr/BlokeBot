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
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    var login = currentSession.Login;
                    if (string.IsNullOrWhiteSpace(login))
                        return Results.Redirect("/auth/login");

                    if (!await siteAccess.CanCreateHostAsync(login, ct))
                        return Results.Forbid();

                    var userId = currentSession.UserId;
                    var displayName = string.IsNullOrWhiteSpace(currentSession.DisplayName)
                        ? login
                        : currentSession.DisplayName;
                    var profileImageUrl = currentSession.ProfileImageUrl;
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
                    var current = currentSession.HostSelection;
                    var available =
                        current?.Available.Where(x => x.Id != host.Id).Append(host).ToArray()
                        ?? [host];

                    await session.SignInHostAsync(
                        context,
                        available,
                        host,
                        currentSession.IsBotAdmin,
                        adminEditingLogin: null
                    );

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/host"));
                }
            )
            .RequireAuthorization();
    }
}
