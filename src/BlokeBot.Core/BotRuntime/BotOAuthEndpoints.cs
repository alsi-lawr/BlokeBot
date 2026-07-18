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
                        ? Result(
                            BlokeBotAuthOutcome.Unavailable,
                            BlokeBotAuthStatus.ServiceUnavailable,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.Admin
                        )
                        : Result(
                            BlokeBotAuthOutcome.AccessRequired,
                            BlokeBotAuthStatus.Forbidden,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.Admin
                        )
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
                        : Result(
                            BlokeBotAuthOutcome.AccessRequired,
                            BlokeBotAuthStatus.Forbidden,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.Admin
                        )
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
                        return Result(
                            BlokeBotAuthOutcome.AccessRequired,
                            BlokeBotAuthStatus.Forbidden,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.Admin
                        );
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return BotAccountProviderErrorResult(error, context, logger);
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return Result(
                            BlokeBotAuthOutcome.Cancelled,
                            BlokeBotAuthStatus.BadRequest,
                            BlokeBotAuthRetryAction.BotAccount,
                            BlokeBotAuthReturnAction.Admin
                        );
                    }

                    if (string.IsNullOrWhiteSpace(state))
                    {
                        return Result(
                            BlokeBotAuthOutcome.InvalidOrExpired,
                            BlokeBotAuthStatus.BadRequest,
                            BlokeBotAuthRetryAction.BotAccount,
                            BlokeBotAuthReturnAction.Admin
                        );
                    }

                    var completion = await oauth.CompleteAuthorizationAsync(code, state, ct);
                    return await completion.Match<Task<IResult>>(
                        async _ =>
                        {
                            await changes.NotifyChangedAsync(ct);
                            return Result(
                                BlokeBotAuthOutcome.Success,
                                BlokeBotAuthStatus.Ok,
                                BlokeBotAuthRetryAction.None,
                                BlokeBotAuthReturnAction.Admin,
                                resultContext: new BlokeBotAuthContext.Success(
                                    BlokeBotAuthSuccessKind.BotAccount
                                )
                            );
                        },
                        static _ =>
                            Task.FromResult<IResult>(
                                Result(
                                    BlokeBotAuthOutcome.InvalidOrExpired,
                                    BlokeBotAuthStatus.BadRequest,
                                    BlokeBotAuthRetryAction.BotAccount,
                                    BlokeBotAuthReturnAction.Admin
                                )
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
                                Result(
                                    BlokeBotAuthOutcome.Unavailable,
                                    BlokeBotAuthStatus.ServiceUnavailable,
                                    BlokeBotAuthRetryAction.None,
                                    BlokeBotAuthReturnAction.ChannelSetup
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
                            BlokeBotAuthRetryAction.ChannelBot,
                            context,
                            logger
                        );
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return Result(
                            BlokeBotAuthOutcome.Cancelled,
                            BlokeBotAuthStatus.BadRequest,
                            BlokeBotAuthRetryAction.ChannelBot,
                            BlokeBotAuthReturnAction.ChannelSetup
                        );
                    }

                    if (
                        string.IsNullOrWhiteSpace(state)
                        || string.IsNullOrWhiteSpace(storedState)
                        || !string.Equals(state, storedState, StringComparison.Ordinal)
                    )
                    {
                        return Result(
                            BlokeBotAuthOutcome.InvalidOrExpired,
                            BlokeBotAuthStatus.BadRequest,
                            BlokeBotAuthRetryAction.ChannelBot,
                            BlokeBotAuthReturnAction.ChannelSetup
                        );
                    }

                    var selectedHost = session.State.Match<BotHostChoice?>(
                        _ => null,
                        selected => selected.Selection.Current,
                        _ => null
                    );
                    if (selectedHost is null)
                    {
                        return Result(
                            BlokeBotAuthOutcome.NoChannelSelected,
                            BlokeBotAuthStatus.Forbidden,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.ChannelSetup
                        );
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
                                    outcome => MapChannelAuthorization(outcome, selectedHost.Login),
                                    _ => throw new UnreachableException()
                                );
                            },
                            configurationUnavailable =>
                                Task.FromResult<IResult>(
                                    ProviderFailureResult(
                                        BlokeBotAuthRetryAction.ChannelBot,
                                        context,
                                        logger,
                                        "ConfigurationUnavailable",
                                        configurationUnavailable.GetType().Name
                                    )
                                ),
                            providerNotValidated =>
                                Task.FromResult<IResult>(
                                    ProviderFailureResult(
                                        BlokeBotAuthRetryAction.ChannelBot,
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
                            BlokeBotAuthRetryAction.ChannelBot,
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
                        return Result(
                            BlokeBotAuthOutcome.NoChannelSelected,
                            BlokeBotAuthStatus.Forbidden,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.ChannelSetup
                        );
                    }

                    if (!await hostBotAuthorization.CanAuthorizeAsync(selectedHost.Id, ct))
                    {
                        return Result(
                            BlokeBotAuthOutcome.CustomBotDisabled,
                            BlokeBotAuthStatus.BadRequest,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.ChannelSetup
                        );
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
                                Result(
                                    BlokeBotAuthOutcome.Unavailable,
                                    BlokeBotAuthStatus.ServiceUnavailable,
                                    BlokeBotAuthRetryAction.None,
                                    BlokeBotAuthReturnAction.ChannelSetup
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
            return ProviderErrorResult(error, BlokeBotAuthRetryAction.HostBot, context, logger);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result(
                BlokeBotAuthOutcome.Cancelled,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.HostBot,
                BlokeBotAuthReturnAction.ChannelSetup
            );
        }

        if (
            string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(storedState)
            || !string.Equals(state, storedState, StringComparison.Ordinal)
        )
        {
            return Result(
                BlokeBotAuthOutcome.InvalidOrExpired,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.HostBot,
                BlokeBotAuthReturnAction.ChannelSetup
            );
        }

        var selectedHost = session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );
        if (selectedHost is null)
        {
            return Result(
                BlokeBotAuthOutcome.NoChannelSelected,
                BlokeBotAuthStatus.Forbidden,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup
            );
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
                        outcome => MapHostBotAuthorization(outcome),
                        _ => throw new UnreachableException()
                    );
                },
                configurationUnavailable =>
                    Task.FromResult<IResult>(
                        ProviderFailureResult(
                            BlokeBotAuthRetryAction.HostBot,
                            context,
                            logger,
                            "ConfigurationUnavailable",
                            configurationUnavailable.GetType().Name
                        )
                    ),
                providerNotValidated =>
                    Task.FromResult<IResult>(
                        ProviderFailureResult(
                            BlokeBotAuthRetryAction.HostBot,
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
                BlokeBotAuthRetryAction.HostBot,
                context,
                logger,
                "TransportFailure",
                exception.GetType().Name
            );
        }
    }

    private static IResult MapChannelAuthorization(
        ChannelBotAuthorizationOutcome outcome,
        string requiredChannelLogin
    )
    {
        return outcome switch
        {
            ChannelBotAuthorizationOutcome.Authorized => Result(
                BlokeBotAuthOutcome.Success,
                BlokeBotAuthStatus.Ok,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                resultContext: new BlokeBotAuthContext.Success(
                    BlokeBotAuthSuccessKind.ChannelConnection
                )
            ),
            ChannelBotAuthorizationOutcome.HostNotFound => Result(
                BlokeBotAuthOutcome.NoChannelSelected,
                BlokeBotAuthStatus.Forbidden,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            ChannelBotAuthorizationOutcome.GrantMismatch => Result(
                BlokeBotAuthOutcome.WrongAccount,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.ChannelBot,
                BlokeBotAuthReturnAction.ChannelSetup,
                resultContext: new BlokeBotAuthContext.RequiredChannel(requiredChannelLogin)
            ),
            ChannelBotAuthorizationOutcome.MissingScopes => Result(
                BlokeBotAuthOutcome.PermissionOrAccount,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.ChannelBot,
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            _ => throw new UnreachableException(),
        };
    }

    private static IResult MapHostBotAuthorization(HostBotAccountAuthorizationOutcome outcome)
    {
        return outcome switch
        {
            HostBotAccountAuthorizationOutcome.Authorized => Result(
                BlokeBotAuthOutcome.Success,
                BlokeBotAuthStatus.Ok,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup,
                resultContext: new BlokeBotAuthContext.Success(
                    BlokeBotAuthSuccessKind.ChannelConnection
                )
            ),
            HostBotAccountAuthorizationOutcome.HostNotFound => Result(
                BlokeBotAuthOutcome.NoChannelSelected,
                BlokeBotAuthStatus.Forbidden,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            HostBotAccountAuthorizationOutcome.OverrideDisabled => Result(
                BlokeBotAuthOutcome.CustomBotDisabled,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.None,
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            HostBotAccountAuthorizationOutcome.MissingScopes => Result(
                BlokeBotAuthOutcome.PermissionOrAccount,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.HostBot,
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            _ => throw new UnreachableException(),
        };
    }

    private static IResult ConnectionAccessResult(AuthenticatedSession session)
    {
        return session.State.Match<IResult>(
            static _ =>
                Result(
                    BlokeBotAuthOutcome.NoChannelSelected,
                    BlokeBotAuthStatus.Forbidden,
                    BlokeBotAuthRetryAction.None,
                    BlokeBotAuthReturnAction.ChannelSetup
                ),
            static _ =>
                Result(
                    BlokeBotAuthOutcome.AccessRequired,
                    BlokeBotAuthStatus.Forbidden,
                    BlokeBotAuthRetryAction.None,
                    BlokeBotAuthReturnAction.ChannelSetup
                ),
            static _ =>
                Result(
                    BlokeBotAuthOutcome.NoChannelSelected,
                    BlokeBotAuthStatus.Forbidden,
                    BlokeBotAuthRetryAction.None,
                    BlokeBotAuthReturnAction.ChannelSetup
                )
        );
    }

    private static IResult ProviderErrorResult(
        string error,
        BlokeBotAuthRetryAction retryAction,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger
    )
    {
        return string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
            ? Result(
                BlokeBotAuthOutcome.Cancelled,
                BlokeBotAuthStatus.BadRequest,
                retryAction,
                BlokeBotAuthReturnAction.ChannelSetup
            )
            : ProviderFailureResult(
                retryAction,
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
            ? Result(
                BlokeBotAuthOutcome.Cancelled,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.BotAccount,
                BlokeBotAuthReturnAction.Admin
            )
            : BotAccountProviderFailureResult(
                context,
                logger,
                "OAuthErrorQuery",
                "OAuthErrorQuery"
            );
    }

    private static IResult ProviderFailureResult(
        BlokeBotAuthRetryAction retryAction,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger,
        string classification,
        string failureType
    )
    {
        LogProviderFailure(logger, classification, failureType, context.TraceIdentifier);
        return Result(
            BlokeBotAuthOutcome.ProviderUnavailable,
            BlokeBotAuthStatus.BadGateway,
            retryAction,
            BlokeBotAuthReturnAction.ChannelSetup,
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
        return Result(
            BlokeBotAuthOutcome.ProviderUnavailable,
            BlokeBotAuthStatus.BadGateway,
            BlokeBotAuthRetryAction.BotAccount,
            BlokeBotAuthReturnAction.Admin,
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

    private static BlokeBotAuthResult Result(
        BlokeBotAuthOutcome outcome,
        BlokeBotAuthStatus status,
        BlokeBotAuthRetryAction retryAction,
        BlokeBotAuthReturnAction returnAction,
        string? supportReference = null,
        BlokeBotAuthContext? resultContext = null
    )
    {
        return new(outcome, status, retryAction, returnAction, supportReference, resultContext);
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
