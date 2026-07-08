using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Alsi.TwitchBot;
using BlokeBot.Auth.Sessions;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Hosts;

namespace BlokeBot.BotRuntime;

internal static class BotOAuthEndpoints
{
    public static void MapBotOAuthEndpoints(this WebApplication app, bool runtimeConfigured)
    {
        if (!runtimeConfigured)
        {
            app.MapGet(
                    "/oauth/start",
                    () => Results.BadRequest("TwitchBot configuration is incomplete.")
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
                    string? code,
                    string? state,
                    string? error,
                    ITwitchOAuthFlow oauth,
                    CancellationToken ct
                ) =>
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        return Results.Content(
                            $"OAuth error: {WebUtility.HtmlEncode(error)}",
                            "text/plain"
                        );

                    if (string.IsNullOrWhiteSpace(code))
                        return Results.BadRequest("Missing code");

                    if (string.IsNullOrWhiteSpace(state))
                        return Results.BadRequest("Invalid state");

                    try
                    {
                        await oauth.CompleteAuthorizationAsync(code, state, ct);
                        return Results.Content(
                            "OK. Tokens saved. You can close this window.",
                            "text/plain"
                        );
                    }
                    catch (InvalidOperationException)
                    {
                        return Results.BadRequest("Invalid state");
                    }
                }
            )
            .RequireAuthorization("BotAdmin");

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
                    if (!IsStreamer(context.User))
                        return Results.Forbid();

                    var selectedHost = BotHostSelectionAccessor
                        .FromPrincipal(context.User)
                        ?.Current;
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
                    if (!IsStreamer(context.User))
                        return Results.Forbid();

                    var storedState = context.Request.Cookies["BlokeBot.ChannelBotState"];
                    DeleteChannelBotStateCookie(context);

                    if (!string.IsNullOrWhiteSpace(error))
                        return Results.Content(
                            $"OAuth error: {WebUtility.HtmlEncode(error)}",
                            "text/plain"
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

                    var selectedHost = BotHostSelectionAccessor
                        .FromPrincipal(context.User)
                        ?.Current;
                    if (selectedHost is null)
                        return Results.BadRequest("Select a hosted channel before authorizing it.");

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
                            "Twitch rejected the channel bot authorization request.",
                            statusCode: StatusCodes.Status502BadGateway,
                            title: "Channel bot authorization failed"
                        );
                    }

                    return Results.Content(
                        "OK. Channel bot authorization granted. You can close this window.",
                        "text/plain"
                    );
                }
            )
            .RequireAuthorization("Operator");
    }

    private static bool IsStreamer(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirstValue(AuthClaims.Role),
            AuthRole.Streamer,
            StringComparison.OrdinalIgnoreCase
        );

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
}
