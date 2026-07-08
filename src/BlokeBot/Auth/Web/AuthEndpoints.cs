using System.Security.Cryptography;
using BlokeBot.Auth.Sessions;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Authentication.Cookies;

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
                    context.Response.Cookies.Append(
                        "BlokeBot.AuthReturnUrl",
                        LocalReturnUrl.OrFallback(returnUrl, "/guessing"),
                        new CookieOptions
                        {
                            HttpOnly = true,
                            IsEssential = true,
                            MaxAge = TimeSpan.FromMinutes(10),
                            SameSite = SameSiteMode.Lax,
                            Secure = context.Request.IsHttps,
                        }
                    );

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
                                : LocalReturnUrl.OrFallback(returnUrl, "/guessing")
                        );
                    }

                    return Results.Redirect(
                        result.User.Hosts.Count == 0
                            ? "/host"
                            : LocalReturnUrl.OrFallback(returnUrl, "/guessing")
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
                    var current = currentSession.HostSelection;
                    if (current is null)
                        return Results.Redirect("/auth/login");

                    var selected = current.Available.FirstOrDefault(host => host.Id == hostId);
                    if (selected is null)
                        return Results.Forbid();

                    if (
                        string.Equals(
                            selected.Role,
                            AuthRole.Moderator,
                            StringComparison.OrdinalIgnoreCase
                        )
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
                        current.Available,
                        selected,
                        currentSession.IsBotAdmin,
                        currentSession.AdminEditingLogin
                    );

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/guessing"));
                }
            )
            .RequireAuthorization("Operator");

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
                    var current = currentSession.HostSelection;
                    if (current is null)
                        return Results.Redirect("/auth/login");

                    if (currentSession.IsBotAccount)
                        return Results.Forbid();

                    var selected = await hostedChannels.LoadHostChoiceAsync(
                        hostId,
                        AuthRole.Admin,
                        context.RequestAborted
                    );
                    if (selected is null)
                        return Results.NotFound();

                    var available = current
                        .Available.Where(x => x.Id != selected.Id)
                        .Append(selected)
                        .ToArray();
                    var returnHost = current.Available.FirstOrDefault(host =>
                        host.Id == selected.Id
                        && !string.Equals(
                            host.Role,
                            AuthRole.Admin,
                            StringComparison.OrdinalIgnoreCase
                        )
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
                    var current = currentSession.HostSelection;
                    if (current is null)
                        return Results.Redirect("/auth/login");

                    var nonAdminHosts = current
                        .Available.Where(host =>
                            !string.Equals(
                                host.Role,
                                AuthRole.Admin,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
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
                                string.Equals(host.Login, login, StringComparison.OrdinalIgnoreCase)
                            )
                            ?? nonAdminHosts.FirstOrDefault();
                    if (selected is null)
                    {
                        await session.SignOutAsync(context);
                        return Results.Redirect("/auth/login");
                    }

                    await session.SignInHostAsync(
                        context,
                        nonAdminHosts,
                        selected,
                        isBotAdmin: true,
                        adminEditingLogin: null
                    );

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/guessing"));
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
}
