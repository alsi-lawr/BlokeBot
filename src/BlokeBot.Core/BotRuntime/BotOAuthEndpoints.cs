using System.Diagnostics;
using System.Security.Cryptography;
using BlokeBot.Core.Auth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Hosts;

namespace BlokeBot.Core.BotRuntime;

internal static class BotOAuthEndpoints
{
    public static void MapUnavailableBotOAuthEndpoint(this WebApplication app)
    {
        app.MapGet(
                "/oauth/start",
                (HttpContext context) =>
                    AuthenticatedSession.FromPrincipal(context.User).IsBotAdmin
                        ? BlokeBotAuthResults.ConnectionUnavailable("/admin", "Return to Admin")
                        : BlokeBotAuthResults.AdministratorAccessRequired()
            )
            .RequireAuthorization();
    }

    public static void MapBotOAuthEndpoints(this WebApplication app)
    {
        var botOAuth = app.MapGroup("/oauth").RequireAuthorization();

        botOAuth
            .MapGet(
                "/start",
                (HttpContext context, IOAuthFlow oauth) =>
                    AuthenticatedSession.FromPrincipal(context.User).IsBotAdmin
                        ? Results.Redirect(oauth.CreateAuthorizationUri().ToString())
                        : BlokeBotAuthResults.AdministratorAccessRequired()
            )
            .RequireAuthorization();

        botOAuth
            .MapGet(
                "/callback",
                async (
                    HttpContext context,
                    string? code,
                    string? state,
                    string? error,
                    IOAuthFlow oauth,
                    HostBotAccountOAuthService hostBotOAuth,
                    HostBotAccountAuthorizationService hostBotAuthorization,
                    HostedChannelChangeNotifier changes,
                    ILogger<BotOAuthEndpointLog> logger,
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
                            logger,
                            ct
                        );
                    }

                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.IsBotAdmin)
                    {
                        return BlokeBotAuthResults.AdministratorAccessRequired();
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return BotAccountProviderErrorResult(error, context, logger);
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return BlokeBotAuthResults.BotAccountConnectionCancelled();
                    }

                    if (string.IsNullOrWhiteSpace(state))
                    {
                        return BlokeBotAuthResults.BotAccountConnectionExpired();
                    }

                    var completion = await oauth.CompleteAuthorizationAsync(code, state, ct);
                    return await completion.Match<Task<IResult>>(
                        async _ =>
                        {
                            await changes.NotifyChangedAsync(ct);
                            return BlokeBotAuthResults.BotAccountConnectionSaved();
                        },
                        static _ =>
                            Task.FromResult<IResult>(
                                BlokeBotAuthResults.BotAccountConnectionExpired()
                            )
                    );
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
                    {
                        return ConnectionAccessResult(session);
                    }

                    var selectedHost = session.State.Match<BotHostChoice?>(
                        _ => null,
                        selected => selected.Selection.Current,
                        _ => null
                    );
                    if (selectedHost is not null)
                    {
                        await channelBotAuthorization.ClearIfScopesStaleAsync(selectedHost.Id, ct);
                    }

                    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    DeleteChannelBotStateCookie(context);
                    return oauth
                        .CreateAuthorization(context.Request, state)
                        .Match<IResult>(
                            ready =>
                            {
                                context.Response.Cookies.Append(
                                    "BlokeBot.ChannelBotState",
                                    state,
                                    ChannelBotStateCookieOptions(
                                        context.Request,
                                        TimeSpan.FromMinutes(10)
                                    )
                                );
                                return Results.Redirect(ready.AuthorizationUri.ToString());
                            },
                            _ =>
                                BlokeBotAuthResults.ConnectionUnavailable(
                                    "/host",
                                    "Return to Channel setup"
                                )
                        );
                }
            )
            .RequireAuthorization();

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
                    ILogger<BotOAuthEndpointLog> logger,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.CanAuthorizeSelectedHost)
                    {
                        return ConnectionAccessResult(session);
                    }

                    var storedState = context.Request.Cookies["BlokeBot.ChannelBotState"];
                    DeleteChannelBotStateCookie(context);

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return ProviderErrorResult(
                            error,
                            "/oauth/channel-bot/start",
                            context,
                            logger
                        );
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return BlokeBotAuthResults.Cancelled("/oauth/channel-bot/start");
                    }

                    if (
                        string.IsNullOrWhiteSpace(state)
                        || string.IsNullOrWhiteSpace(storedState)
                        || !string.Equals(state, storedState, StringComparison.Ordinal)
                    )
                    {
                        return BlokeBotAuthResults.Expired("/oauth/channel-bot/start");
                    }

                    var selectedHost = session.State.Match<BotHostChoice?>(
                        _ => null,
                        selected => selected.Selection.Current,
                        _ => null
                    );
                    if (selectedHost is null)
                    {
                        return BlokeBotAuthResults.NoChannelSelected();
                    }

                    try
                    {
                        var completion = await oauth.CompleteAsync(context.Request, code, ct);
                        return await completion.Match<Task<IResult>>(
                            async completed =>
                            {
                                var authorization = await channelBotAuthorization
                                    .Authorize(selectedHost.Id, completed.Grant)
                                    .ExecuteAsync(ct);
                                return authorization.Match(
                                    outcome =>
                                        MapChannelAuthorization(
                                            outcome,
                                            selectedHost.Login,
                                            "/oauth/channel-bot/start"
                                        ),
                                    _ => throw new UnreachableException()
                                );
                            },
                            configurationUnavailable =>
                                Task.FromResult<IResult>(
                                    ProviderFailureResult(
                                        "/oauth/channel-bot/start",
                                        context,
                                        logger,
                                        "ConfigurationUnavailable",
                                        configurationUnavailable.GetType().Name
                                    )
                                ),
                            providerNotValidated =>
                                Task.FromResult<IResult>(
                                    ProviderFailureResult(
                                        "/oauth/channel-bot/start",
                                        context,
                                        logger,
                                        "ProviderNotValidated",
                                        providerNotValidated.GetType().Name
                                    )
                                )
                        );
                    }
                    catch (HttpRequestException exception)
                    {
                        return ProviderFailureResult(
                            "/oauth/channel-bot/start",
                            context,
                            logger,
                            "TransportFailure",
                            exception.GetType().Name
                        );
                    }
                }
            )
            .RequireAuthorization();

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
                    {
                        return ConnectionAccessResult(session);
                    }

                    var selectedHost = session.State.Match<BotHostChoice?>(
                        _ => null,
                        selected => selected.Selection.Current,
                        _ => null
                    );
                    if (selectedHost is null)
                    {
                        return BlokeBotAuthResults.NoChannelSelected();
                    }

                    if (!await hostBotAuthorization.CanAuthorizeAsync(selectedHost.Id, ct))
                    {
                        return BlokeBotAuthResults.CustomBotMustBeEnabled();
                    }

                    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    DeleteHostBotStateCookie(context);
                    var requiredScopes = await hostBotAuthorization.GetRequiredScopesAsync(
                        selectedHost.Id,
                        ct
                    );
                    return oauth
                        .CreateAuthorizationUriForScopes(
                            state,
                            OAuthAuthorizationScopeSet.Create(requiredScopes)
                        )
                        .Match<IResult>(
                            ready =>
                            {
                                context.Response.Cookies.Append(
                                    "BlokeBot.HostBotState",
                                    state,
                                    HostBotStateCookieOptions(
                                        context.Request,
                                        TimeSpan.FromMinutes(10)
                                    )
                                );
                                return Results.Redirect(ready.AuthorizationUri.ToString());
                            },
                            static _ =>
                                BlokeBotAuthResults.ConnectionUnavailable(
                                    "/host",
                                    "Return to Channel setup"
                                )
                        );
                }
            )
            .RequireAuthorization();
    }

    private static async Task<IResult> CompleteHostBotAuthorizationAsync(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        HostBotAccountOAuthService oauth,
        HostBotAccountAuthorizationService hostBotAuthorization,
        ILogger<BotOAuthEndpointLog> logger,
        CancellationToken ct
    )
    {
        var session = AuthenticatedSession.FromPrincipal(context.User);
        if (!session.CanAuthorizeSelectedHost)
        {
            return ConnectionAccessResult(session);
        }

        var storedState = context.Request.Cookies["BlokeBot.HostBotState"];
        DeleteHostBotStateCookie(context);

        if (!string.IsNullOrWhiteSpace(error))
        {
            return ProviderErrorResult(error, "/oauth/host-bot/start", context, logger);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BlokeBotAuthResults.Cancelled("/oauth/host-bot/start");
        }

        if (
            string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(storedState)
            || !string.Equals(state, storedState, StringComparison.Ordinal)
        )
        {
            return BlokeBotAuthResults.Expired("/oauth/host-bot/start");
        }

        var selectedHost = session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        if (selectedHost is null)
        {
            return BlokeBotAuthResults.NoChannelSelected();
        }

        try
        {
            var completion = await oauth.CompleteAsync(code, ct);
            return await completion.Match<Task<IResult>>(
                async completed =>
                {
                    var authorization = await hostBotAuthorization
                        .Authorize(selectedHost.Id, completed.Grant)
                        .ExecuteAsync(ct);
                    return authorization.Match(
                        outcome => MapHostBotAuthorization(outcome, "/oauth/host-bot/start"),
                        _ => throw new UnreachableException()
                    );
                },
                configurationUnavailable =>
                    Task.FromResult<IResult>(
                        ProviderFailureResult(
                            "/oauth/host-bot/start",
                            context,
                            logger,
                            "ConfigurationUnavailable",
                            configurationUnavailable.GetType().Name
                        )
                    ),
                providerNotValidated =>
                    Task.FromResult<IResult>(
                        ProviderFailureResult(
                            "/oauth/host-bot/start",
                            context,
                            logger,
                            "ProviderNotValidated",
                            providerNotValidated.GetType().Name
                        )
                    )
            );
        }
        catch (HttpRequestException exception)
        {
            return ProviderFailureResult(
                "/oauth/host-bot/start",
                context,
                logger,
                "TransportFailure",
                exception.GetType().Name
            );
        }
    }

    private static IResult MapChannelAuthorization(
        ChannelBotAuthorizationOutcome outcome,
        string requiredChannelLogin,
        string tryAgainUrl
    )
    {
        return outcome switch
        {
            ChannelBotAuthorizationOutcome.Authorized => BlokeBotAuthResults.ConnectionSaved(
                "/host",
                "Return to Channel setup"
            ),
            ChannelBotAuthorizationOutcome.HostNotFound => BlokeBotAuthResults.NoChannelSelected(),
            ChannelBotAuthorizationOutcome.GrantMismatch => BlokeBotAuthResults.WrongChannelAccount(
                requiredChannelLogin,
                tryAgainUrl
            ),
            ChannelBotAuthorizationOutcome.MissingScopes => BlokeBotAuthResults.PermissionNeeded(
                tryAgainUrl
            ),
            _ => throw new UnreachableException(),
        };
    }

    private static IResult MapHostBotAuthorization(
        HostBotAccountAuthorizationOutcome outcome,
        string tryAgainUrl
    )
    {
        return outcome switch
        {
            HostBotAccountAuthorizationOutcome.Authorized => BlokeBotAuthResults.ConnectionSaved(
                "/host",
                "Return to Channel setup"
            ),
            HostBotAccountAuthorizationOutcome.HostNotFound =>
                BlokeBotAuthResults.NoChannelSelected(),
            HostBotAccountAuthorizationOutcome.OverrideDisabled =>
                BlokeBotAuthResults.CustomBotMustBeEnabled(),
            HostBotAccountAuthorizationOutcome.MissingScopes =>
                BlokeBotAuthResults.PermissionNeeded(tryAgainUrl),
            _ => throw new UnreachableException(),
        };
    }

    private static IResult ConnectionAccessResult(AuthenticatedSession session)
    {
        return session.State.Match<IResult>(
            static _ => BlokeBotAuthResults.NoChannelSelected(),
            static _ => BlokeBotAuthResults.OperatorAccessRequired(),
            static _ => BlokeBotAuthResults.NoChannelSelected()
        );
    }

    private static IResult ProviderErrorResult(
        string error,
        string tryAgainUrl,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger
    )
    {
        return string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
            ? BlokeBotAuthResults.Cancelled(tryAgainUrl)
            : ProviderFailureResult(
                tryAgainUrl,
                context,
                logger,
                "OAuthErrorQuery",
                "OAuthErrorQuery"
            );
    }

    private static IResult BotAccountProviderErrorResult(
        string error,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger
    )
    {
        return string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
            ? BlokeBotAuthResults.BotAccountConnectionCancelled()
            : BotAccountProviderFailureResult(
                context,
                logger,
                "OAuthErrorQuery",
                "OAuthErrorQuery"
            );
    }

    private static IResult ProviderFailureResult(
        string tryAgainUrl,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger,
        string classification,
        string failureType
    )
    {
        LogProviderFailure(logger, classification, failureType, context.TraceIdentifier);
        return BlokeBotAuthResults.ProviderTemporarilyUnavailable(
            tryAgainUrl,
            context.TraceIdentifier
        );
    }

    private static IResult BotAccountProviderFailureResult(
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger,
        string classification,
        string failureType
    )
    {
        LogProviderFailure(logger, classification, failureType, context.TraceIdentifier);
        return BlokeBotAuthResults.BotAccountProviderTemporarilyUnavailable(
            context.TraceIdentifier
        );
    }

    private static void LogProviderFailure(
        ILogger<BotOAuthEndpointLog> logger,
        string classification,
        string failureType,
        string supportReference
    )
    {
        logger.LogWarning(
            "Twitch bot OAuth failed; Classification: {Classification}; FailureType: {FailureType}; SupportReference: {SupportReference}.",
            classification,
            failureType,
            supportReference
        );
    }

    private static CookieOptions ChannelBotStateCookieOptions(HttpRequest request, TimeSpan? maxAge)
    {
        return new()
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = maxAge,
            Path = "/oauth/channel-bot",
            SameSite = SameSiteMode.Lax,
            Secure = request.IsHttps,
        };
    }

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

    private static CookieOptions HostBotStateCookieOptions(HttpRequest request, TimeSpan? maxAge)
    {
        return new()
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = maxAge,
            Path = "/oauth",
            SameSite = SameSiteMode.Lax,
            Secure = request.IsHttps,
        };
    }

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

    private sealed class BotOAuthEndpointLog;
}
