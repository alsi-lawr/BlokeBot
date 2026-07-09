using System.Net;
using System.Security.Cryptography;
using BlokeBot.Auth.Sessions;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;

namespace BlokeBot.BotRuntime;

internal static class BotOAuthEndpoints
{
    public static void MapBotOAuthEndpoints(this WebApplication app, bool runtimeConfigured)
    {
        if (!runtimeConfigured)
        {
            app.MapGet(
                    "/oauth/start",
                    () => Results.BadRequest("The bot account is not set up yet.")
                )
                .RequireAuthorization("BotAdmin");
            return;
        }

        var botOAuth = app.MapGroup("/oauth").RequireAuthorization();

        botOAuth
            .MapGet(
                "/start",
                (ITwitchOAuthFlow oauth) =>
                    Results.Redirect(oauth.CreateAuthorizationUri().ToString())
            )
            .RequireAuthorization("BotAdmin");

        botOAuth
            .MapGet(
                "/callback",
                async (
                    HttpContext context,
                    string? code,
                    string? state,
                    string? error,
                    ITwitchOAuthFlow oauth,
                    HostBotAccountOAuthService hostBotOAuth,
                    HostBotAccountAuthorizationService hostBotAuthorization,
                    HostedChannelChangeNotifier changes,
                    CancellationToken ct
                ) =>
                {
                    if (context.Request.Cookies["BlokeBot.HostBotState"] is { Length: > 0 })
                    {
                        return await CompleteHostBotAuthorizationAsync(
                            context,
                            code,
                            state,
                            error,
                            hostBotOAuth,
                            hostBotAuthorization,
                            ct
                        );
                    }

                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.IsBotAdmin)
                        return Results.Forbid();

                    if (!string.IsNullOrWhiteSpace(error))
                        return Results.Content(
                            $"Twitch could not finish this connection: {WebUtility.HtmlEncode(error)}",
                            "text/plain"
                        );

                    if (string.IsNullOrWhiteSpace(code))
                        return Results.BadRequest("Twitch sign-in did not finish. Try again.");

                    if (string.IsNullOrWhiteSpace(state))
                        return Results.BadRequest("This Twitch sign-in expired. Try again.");

                    try
                    {
                        await oauth.CompleteAuthorizationAsync(code, state, ct);
                        await changes.NotifyChangedAsync();
                        return Results.Content(
                            """
                            <!doctype html>
                            <html lang="en">
                            <head>
                                <meta charset="utf-8">
                                <title>BlokeBot connection complete</title>
                            </head>
                            <body>
                                <p>Bot account connected. You can close this window.</p>
                                <script>
                                    window.close();
                                </script>
                            </body>
                            </html>
                            """,
                            "text/html"
                        );
                    }
                    catch (InvalidOperationException)
                    {
                        return Results.BadRequest("This Twitch sign-in expired. Try again.");
                    }
                }
            )
            .RequireAuthorization();

        botOAuth
            .MapGet(
                "/channel-bot/start",
                async (
                    HttpContext context,
                    ChannelBotOAuthService oauth,
                    ChannelBotAuthorizationService channelBotAuthorization,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.CanAuthorizeSelectedHost)
                        return Results.Forbid();

                    var selectedHost = session.HostSelection?.Current;
                    if (selectedHost is not null)
                        await channelBotAuthorization.ClearIfScopesStaleAsync(selectedHost.Id, ct);

                    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    DeleteChannelBotStateCookie(context);
                    context.Response.Cookies.Append(
                        "BlokeBot.ChannelBotState",
                        state,
                        ChannelBotStateCookieOptions(context.Request, TimeSpan.FromMinutes(10))
                    );

                    try
                    {
                        return Results.Redirect(
                            oauth.CreateAuthorizationUri(context.Request, state).ToString()
                        );
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(ex.Message);
                    }
                }
            )
            .RequireAuthorization("Operator");

        botOAuth
            .MapGet(
                "/channel-bot/callback",
                async (
                    HttpContext context,
                    string? code,
                    string? state,
                    string? error,
                    ChannelBotOAuthService oauth,
                    ChannelBotAuthorizationService channelBotAuthorization,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.CanAuthorizeSelectedHost)
                        return Results.Forbid();

                    var storedState = context.Request.Cookies["BlokeBot.ChannelBotState"];
                    DeleteChannelBotStateCookie(context);

                    if (!string.IsNullOrWhiteSpace(error))
                        return Results.Content(
                            $"Twitch could not finish this connection: {WebUtility.HtmlEncode(error)}",
                            "text/plain"
                        );

                    if (string.IsNullOrWhiteSpace(code))
                        return Results.BadRequest("Twitch connection did not finish. Try again.");

                    if (
                        string.IsNullOrWhiteSpace(state)
                        || string.IsNullOrWhiteSpace(storedState)
                        || !string.Equals(state, storedState, StringComparison.Ordinal)
                    )
                    {
                        return Results.BadRequest("This Twitch connection expired. Try again.");
                    }

                    var selectedHost = session.HostSelection?.Current;
                    if (selectedHost is null)
                        return Results.BadRequest("Choose your channel before connecting it.");

                    try
                    {
                        var grant = await oauth.CompleteAsync(context.Request, code, ct);
                        var authorization = await channelBotAuthorization.AuthorizeAsync(
                            selectedHost.Id,
                            grant,
                            ct
                        );
                        if (!authorization.Succeeded)
                            return Results.BadRequest(authorization.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(ex.Message);
                    }
                    catch (HttpRequestException)
                    {
                        return Results.Problem(
                            "Twitch could not finish connecting this channel.",
                            statusCode: StatusCodes.Status502BadGateway,
                            title: "Channel connection failed"
                        );
                    }

                    return Results.Content(
                        """
                        <!doctype html>
                        <html lang="en">
                        <head>
                            <meta charset="utf-8">
                            <title>BlokeBot connection complete</title>
                        </head>
                        <body>
                            <p>Channel connected. You can close this window.</p>
                            <script>
                                window.close();
                            </script>
                        </body>
                        </html>
                        """,
                        "text/html"
                    );
                }
            )
            .RequireAuthorization("Operator");

        botOAuth
            .MapGet(
                "/host-bot/start",
                async (
                    HttpContext context,
                    HostBotAccountOAuthService oauth,
                    HostBotAccountAuthorizationService hostBotAuthorization,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.CanAuthorizeSelectedHost)
                        return Results.Forbid();

                    var selectedHost = session.HostSelection?.Current;
                    if (selectedHost is null)
                        return Results.BadRequest("Choose your channel before connecting it.");

                    if (!await hostBotAuthorization.CanAuthorizeAsync(selectedHost.Id, ct))
                        return Results.BadRequest("Turn on custom bot before connecting it.");

                    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    DeleteHostBotStateCookie(context);
                    context.Response.Cookies.Append(
                        "BlokeBot.HostBotState",
                        state,
                        HostBotStateCookieOptions(context.Request, TimeSpan.FromMinutes(10))
                    );

                    try
                    {
                        var requiredScopes = await hostBotAuthorization.GetRequiredScopesAsync(
                            selectedHost.Id,
                            ct
                        );
                        return Results.Redirect(
                            oauth.CreateAuthorizationUri(state, requiredScopes).ToString()
                        );
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(ex.Message);
                    }
                }
            )
            .RequireAuthorization("Operator");
    }

    private static async Task<IResult> CompleteHostBotAuthorizationAsync(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        HostBotAccountOAuthService oauth,
        HostBotAccountAuthorizationService hostBotAuthorization,
        CancellationToken ct
    )
    {
        var session = AuthenticatedSession.FromPrincipal(context.User);
        if (!session.CanAuthorizeSelectedHost)
            return Results.Forbid();

        var storedState = context.Request.Cookies["BlokeBot.HostBotState"];
        DeleteHostBotStateCookie(context);

        if (!string.IsNullOrWhiteSpace(error))
            return Results.Content(
                $"Twitch could not finish this connection: {WebUtility.HtmlEncode(error)}",
                "text/plain"
            );

        if (string.IsNullOrWhiteSpace(code))
            return Results.BadRequest("Twitch connection did not finish. Try again.");

        if (
            string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(storedState)
            || !string.Equals(state, storedState, StringComparison.Ordinal)
        )
        {
            return Results.BadRequest("This Twitch connection expired. Try again.");
        }

        var selectedHost = session.HostSelection?.Current;
        if (selectedHost is null)
            return Results.BadRequest("Choose your channel before connecting it.");

        try
        {
            var grant = await oauth.CompleteAsync(code, ct);
            var authorization = await hostBotAuthorization.AuthorizeAsync(
                selectedHost.Id,
                grant,
                ct
            );
            if (!authorization.Succeeded)
                return Results.BadRequest(authorization.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(ex.Message);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                "Twitch could not finish connecting the custom bot.",
                statusCode: StatusCodes.Status502BadGateway,
                title: "Custom bot connection failed"
            );
        }

        return Results.Content(
            """
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <title>BlokeBot connection complete</title>
            </head>
            <body>
                <p>Bot account connected. You can close this window.</p>
                <script>
                    window.close();
                </script>
            </body>
            </html>
            """,
            "text/html"
        );
    }

    private static CookieOptions ChannelBotStateCookieOptions(
        HttpRequest request,
        TimeSpan? maxAge
    ) =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = maxAge,
            Path = "/oauth/channel-bot",
            SameSite = SameSiteMode.Lax,
            Secure = request.IsHttps,
        };

    private static void DeleteChannelBotStateCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(
            "BlokeBot.ChannelBotState",
            ChannelBotStateCookieOptions(context.Request, null)
        );
        context.Response.Cookies.Delete(
            "BlokeBot.ChannelBotState",
            new CookieOptions { Path = "/", Secure = context.Request.IsHttps }
        );
    }

    private static CookieOptions HostBotStateCookieOptions(HttpRequest request, TimeSpan? maxAge) =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = maxAge,
            Path = "/oauth",
            SameSite = SameSiteMode.Lax,
            Secure = request.IsHttps,
        };

    private static void DeleteHostBotStateCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(
            "BlokeBot.HostBotState",
            HostBotStateCookieOptions(context.Request, null)
        );
        context.Response.Cookies.Delete(
            "BlokeBot.HostBotState",
            new CookieOptions { Path = "/", Secure = context.Request.IsHttps }
        );
        context.Response.Cookies.Delete(
            "BlokeBot.HostBotState",
            new CookieOptions { Path = "/oauth/host-bot", Secure = context.Request.IsHttps }
        );
    }
}
