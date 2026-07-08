using System.Security.Cryptography;
using BlokeBot.Auth.Sessions;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Hosts;

namespace BlokeBot.Auth.Web;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/auth/login",
                (HttpContext context, WebAuthService auth, bool? start, string? returnUrl) =>
                {
                    var currentOptions = auth.CurrentOptions;
                    if (!auth.IsConfigured(currentOptions))
                        return Results.Content(
                            LoginPage.Render("Twitch web authentication is not configured."),
                            "text/html",
                            statusCode: StatusCodes.Status503ServiceUnavailable
                        );

                    if (start != true)
                        return Results.Content(LoginPage.Render(), "text/html");

                    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    context.Response.Cookies.Append(
                        "BlokeBot.AuthState",
                        state,
                        new CookieOptions
                        {
                            HttpOnly = true,
                            IsEssential = true,
                            MaxAge = TimeSpan.FromMinutes(10),
                            SameSite = SameSiteMode.Lax,
                            Secure = context.Request.IsHttps,
                        }
                    );
                    if (LocalReturnUrl.IsSafe(returnUrl))
                    {
                        context.Response.Cookies.Append(
                            "BlokeBot.AuthReturnUrl",
                            returnUrl!,
                            new CookieOptions
                            {
                                HttpOnly = true,
                                IsEssential = true,
                                MaxAge = TimeSpan.FromMinutes(10),
                                SameSite = SameSiteMode.Lax,
                                Secure = context.Request.IsHttps,
                            }
                        );
                    }
                    else
                    {
                        context.Response.Cookies.Delete("BlokeBot.AuthReturnUrl");
                    }

                    return Results.Redirect(
                        auth.CreateAuthorizationUri(context.Request, state).ToString()
                    );
                }
            )
            .AllowAnonymous();

        app.MapGet(
                "/auth/twitch/callback",
                async (
                    HttpContext context,
                    string? code,
                    string? state,
                    string? error,
                    WebAuthService auth,
                    AuthSessionService session,
                    CancellationToken cancellationToken
                ) =>
                {
                    var storedState = context.Request.Cookies["BlokeBot.AuthState"];
                    var returnUrl = context.Request.Cookies["BlokeBot.AuthReturnUrl"];
                    context.Response.Cookies.Delete("BlokeBot.AuthState");
                    context.Response.Cookies.Delete("BlokeBot.AuthReturnUrl");

                    if (!string.IsNullOrWhiteSpace(error))
                        return Results.Content(
                            LoginPage.Render(error),
                            "text/html",
                            statusCode: StatusCodes.Status400BadRequest
                        );

                    if (string.IsNullOrWhiteSpace(code))
                        return Results.BadRequest("Missing code");

                    if (
                        string.IsNullOrWhiteSpace(state)
                        || string.IsNullOrWhiteSpace(storedState)
                        || !string.Equals(state, storedState, StringComparison.Ordinal)
                    )
                    {
                        return Results.BadRequest("Invalid state");
                    }

                    AuthResult result;
                    try
                    {
                        result = await auth.AuthenticateAsync(
                            context.Request,
                            code,
                            cancellationToken
                        );
                    }
                    catch (HttpRequestException)
                    {
                        return Results.Problem(
                            "Twitch rejected the authentication request.",
                            statusCode: StatusCodes.Status502BadGateway,
                            title: "Twitch authentication failed"
                        );
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.Problem(
                            ex.Message,
                            statusCode: StatusCodes.Status502BadGateway,
                            title: "Twitch authentication failed"
                        );
                    }

                    if (!result.IsAuthorized || result.User is null)
                        return Results.Content(
                            LoginPage.Render(
                                result.Error
                                    ?? "This Twitch account is not connected to a BlokeBot channel."
                            ),
                            "text/html",
                            statusCode: StatusCodes.Status403Forbidden
                        );

                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    await session.SignInAsync(
                        context,
                        result.User,
                        currentSession.HostSelection?.Current.Id
                    );
                    if (session.IsConfiguredBotAccount(result.User.Login))
                    {
                        return Results.Redirect(
                            string.IsNullOrWhiteSpace(returnUrl)
                                ? "/admin"
                                : LocalReturnUrl.OrFallback(returnUrl, "/admin")
                        );
                    }

                    return Results.Redirect(
                        LocalReturnUrl.OrFallback(returnUrl, DefaultReturnUrl(result.User))
                    );
                }
            )
            .AllowAnonymous();

        app.MapGet(
                "/auth/select-host",
                async (
                    HttpContext context,
                    int hostId,
                    string? returnUrl,
                    AuthSessionService session,
                    HostModAccessService modAccess
                ) =>
                {
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    var available = currentSession.AvailableHosts;
                    if (available.Count == 0)
                        return Results.Redirect("/auth/login");

                    var selected = available.FirstOrDefault(host => host.Id == hostId);
                    if (selected is null)
                        return Results.Forbid();

                    if (
                        selected.Role == AuthRole.Moderator
                        && !await modAccess.CanModeratorAccessAsync(
                            selected.Id,
                            currentSession.Login,
                            context.RequestAborted
                        )
                    )
                    {
                        return Results.Forbid();
                    }

                    await session.SignInHostAsync(
                        context,
                        available,
                        selected,
                        currentSession.IsBotAdmin,
                        currentSession.AdminEditingLogin
                    );

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/guessing"));
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/auth/select-own-host",
                async (HttpContext context, string? returnUrl, AuthSessionService session) =>
                {
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    if (currentSession.IsBotAccount)
                        return Results.Forbid();

                    var ownHost = currentSession.AvailableHosts.FirstOrDefault(host =>
                        host.Role == AuthRole.Streamer
                        && string.Equals(
                            host.Login,
                            currentSession.Login,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                    if (ownHost is not null)
                    {
                        await session.SignInHostAsync(
                            context,
                            currentSession.AvailableHosts,
                            ownHost,
                            currentSession.IsBotAdmin,
                            currentSession.AdminEditingLogin
                        );

                        return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/guessing"));
                    }

                    if (!currentSession.CanCreateHost)
                        return Results.Forbid();

                    await session.SignInHostSelectionAsync(
                        context,
                        currentSession.AvailableHosts,
                        selectedHost: null,
                        currentSession.IsBotAdmin,
                        currentSession.AdminEditingLogin
                    );

                    return Results.Redirect("/host");
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/admin/select-host",
                async (
                    HttpContext context,
                    int hostId,
                    string? returnUrl,
                    HostedChannelDirectoryService hostedChannels,
                    AuthSessionService session
                ) =>
                {
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);

                    if (currentSession.IsBotAccount)
                        return Results.Forbid();

                    var selected = await hostedChannels.LoadHostChoiceAsync(
                        hostId,
                        AuthRole.Admin,
                        context.RequestAborted
                    );
                    if (selected is null)
                        return Results.NotFound();

                    var available = currentSession
                        .AvailableHosts.Where(x => x.Id != selected.Id)
                        .Append(selected)
                        .ToArray();
                    var returnHost = currentSession.AvailableHosts.FirstOrDefault(host =>
                        host.Id == selected.Id && host.Role != AuthRole.Admin
                    );
                    await session.SignInHostAsync(
                        context,
                        available,
                        selected,
                        isBotAdmin: true,
                        adminEditingLogin: currentSession.Login,
                        adminReturnHost: returnHost
                    );

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/guessing"));
                }
            )
            .RequireAuthorization("BotAdmin");

        app.MapGet(
                "/auth/exit-admin",
                async (HttpContext context, string? returnUrl, AuthSessionService session) =>
                {
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    var nonAdminHosts = currentSession
                        .AvailableHosts.Where(host => host.Role != AuthRole.Admin)
                        .ToArray();
                    var returnHost = currentSession.AdminReturnHost;
                    if (
                        returnHost is not null
                        && nonAdminHosts.All(host => host.Id != returnHost.Id)
                    )
                        nonAdminHosts = [.. nonAdminHosts, returnHost];

                    var login = currentSession.Login;
                    var selected = returnHost is not null
                        ? nonAdminHosts.FirstOrDefault(host => host.Id == returnHost.Id)
                        : null
                            ?? nonAdminHosts.FirstOrDefault(host =>
                                host.Role == AuthRole.Streamer
                                && string.Equals(
                                    host.Login,
                                    login,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            ?? (
                                currentSession.CanCreateHost ? null : nonAdminHosts.FirstOrDefault()
                            );
                    if (selected is null && !currentSession.CanCreateHost)
                    {
                        await session.SignOutAsync(context);
                        return Results.Redirect("/auth/login");
                    }

                    await session.SignInHostSelectionAsync(
                        context,
                        nonAdminHosts,
                        selected,
                        isBotAdmin: true,
                        adminEditingLogin: null
                    );

                    return Results.Redirect(
                        LocalReturnUrl.OrFallback(
                            returnUrl,
                            selected is null ? "/host" : "/guessing"
                        )
                    );
                }
            )
            .RequireAuthorization("BotAdmin");

        app.MapGet(
                "/auth/logout",
                async (HttpContext context, AuthSessionService session) =>
                {
                    await session.SignOutAsync(context);
                    return Results.Redirect("/auth/login");
                }
            )
            .AllowAnonymous();
    }

    private static string DefaultReturnUrl(AuthenticatedUser user)
    {
        if (user.CanCreateHost && !HasOwnHostedChannel(user))
            return "/host";

        return user.Hosts.Count == 0 ? "/host" : "/guessing";
    }

    private static bool HasOwnHostedChannel(AuthenticatedUser user) =>
        user.Hosts.Any(host =>
            host.Role == AuthRole.Streamer
            && string.Equals(host.Login, user.Login, StringComparison.OrdinalIgnoreCase)
        );
}
