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
    public static void MapUnavailableBotOAuthEndpoint(this WebApplication app) =>
        app.MapGet(
                "/oauth/start",
                static (HttpContext context) =>
                    AuthenticatedSession.FromPrincipal(context.User).IsBotAdmin
                        ? Unavailable(BlokeBotAuthReturnAction.Admin)
                        : AccessRequired(BlokeBotAuthReturnAction.Admin)
            )
            .RequireAuthorization();

    public static void MapBotOAuthEndpoints(this WebApplication app)
    {
        var botOAuth = app.MapGroup("/oauth").RequireAuthorization();

        _ = botOAuth
            .MapGet(
                "/start",
                async (
                    HttpContext context,
                    IOAuthFlow oauth,
                    HostBotAccountAuthorizationService hostBotAuthorization,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.IsBotAdmin)
                    {
                        return AccessRequired(BlokeBotAuthReturnAction.Admin);
                    }

                    var selectedHost = SelectedHost(session);
                    var authorizationUri = selectedHost is null
                        ? oauth.CreateAuthorizationUri()
                        : oauth.CreateAuthorizationUri(
                            await hostBotAuthorization.GetRequiredScopesAsync(selectedHost.Id, ct)
                        );
                    return Results.Redirect(authorizationUri.ToString());
                }
            )
            .RequireAuthorization();

        _ = botOAuth
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
                    HostBotOAuthStateStore hostBotStates,
                    HostedChannelChangeNotifier changes,
                    ILogger<BotOAuthEndpointLog> logger,
                    CancellationToken ct
                ) =>
                {
                    if (HostBroadcasterOAuthStateStore.IsState(state))
                    {
                        return await CompleteBroadcasterAuthorizationAsync(
                            context,
                            code,
                            state,
                            error,
                            hostBotOAuth,
                            app.Services.GetRequiredService<HostBroadcasterAuthorizationService>(),
                            app.Services.GetRequiredService<HostBroadcasterOAuthStateStore>(),
                            logger,
                            ct
                        );
                    }

                    if (HostBotOAuthStateStore.IsHostBotState(state))
                    {
                        return await CompleteHostBotAuthorizationAsync(
                            context,
                            code,
                            state,
                            error,
                            hostBotOAuth,
                            hostBotAuthorization,
                            hostBotStates,
                            logger,
                            ct
                        );
                    }

                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.IsBotAdmin)
                    {
                        return AccessRequired(BlokeBotAuthReturnAction.Admin);
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return BotAccountProviderErrorResult(error, context, logger);
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return Cancelled(
                            BlokeBotAuthRetryAction.BotAccount,
                            BlokeBotAuthReturnAction.Admin
                        );
                    }

                    if (string.IsNullOrWhiteSpace(state))
                    {
                        return InvalidOrExpired(
                            BlokeBotAuthRetryAction.BotAccount,
                            BlokeBotAuthReturnAction.Admin
                        );
                    }

                    var completion = await oauth.CompleteAuthorizationAsync(code, state, ct);
                    return await completion.Match<Task<IResult>>(
                        async completed =>
                        {
                            _ = await changes.NotifyChangedAsync(ct);
                            return Success(
                                BlokeBotAuthReturnAction.Admin,
                                BlokeBotAuthSuccessKind.BotAccount
                            );
                        },
                        static _ =>
                            Task.FromResult<IResult>(
                                InvalidOrExpired(
                                    BlokeBotAuthRetryAction.BotAccount,
                                    BlokeBotAuthReturnAction.Admin
                                )
                            )
                    );
                }
            )
            .RequireAuthorization();

        _ = botOAuth
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

                    var selectedHost = SelectedHost(session);
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
                            _ => Unavailable(BlokeBotAuthReturnAction.ChannelSetup)
                        );
                }
            )
            .RequireAuthorization();

        _ = botOAuth
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
                        return Cancelled(
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
                        return InvalidOrExpired(
                            BlokeBotAuthRetryAction.ChannelBot,
                            BlokeBotAuthReturnAction.ChannelSetup
                        );
                    }

                    var selectedHost = SelectedHost(session);
                    if (selectedHost is null)
                    {
                        return NoChannelSelected();
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

        _ = botOAuth
            .MapGet(
                "/host-bot/start",
                async (
                    HttpContext context,
                    HostBotAccountOAuthService oauth,
                    HostBotAccountAuthorizationService hostBotAuthorization,
                    HostBotOAuthStateStore hostBotStates,
                    CancellationToken ct
                ) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    if (!session.CanAuthorizeSelectedHostBotAccount)
                    {
                        return ConnectionAccessResult(session);
                    }

                    var selectedHost = SelectedHost(session);
                    if (selectedHost is null)
                    {
                        return NoChannelSelected();
                    }

                    var actor = HostBotAccountActorFor(session);
                    if (!await hostBotAuthorization.CanAuthorizeAsync(selectedHost.Id, actor, ct))
                    {
                        return CustomBotDisabled();
                    }

                    var state = hostBotStates.Issue(session.UserId, selectedHost.Id);
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
                            ready => Results.Redirect(ready.AuthorizationUri.ToString()),
                            static _ => Unavailable(BlokeBotAuthReturnAction.ChannelSetup)
                        );
                }
            )
            .RequireAuthorization();

        _ = botOAuth
            .MapGet(
                "/broadcaster/start",
                (HttpContext context) =>
                {
                    var session = AuthenticatedSession.FromPrincipal(context.User);
                    var selected = SelectedHost(session);
                    var oauth = context.RequestServices.GetService<HostBotAccountOAuthService>();
                    var states =
                        context.RequestServices.GetService<HostBroadcasterOAuthStateStore>();
                    return (session.CanAuthorizeSelectedHost, selected, oauth, states) switch
                    {
                        (false, _, _, _) or (_, null, _, _) => ConnectionAccessResult(session),
                        (_, _, null, _) or (_, _, _, null) => Unavailable(
                            BlokeBotAuthReturnAction.ChannelSetup
                        ),
                        (_, { } selectedHost, { } readyOauth, { } readyStates) => readyOauth
                            .CreateAuthorizationUriForScopes(
                                readyStates.Issue(session.UserId, selectedHost.Id),
                                OAuthAuthorizationScopeSet.Create(
                                    HostBroadcasterAuthorizationService.MilestoneScopes
                                )
                            )
                            .Match<IResult>(
                                ready => Results.Redirect(ready.AuthorizationUri.ToString()),
                                _ => Unavailable(BlokeBotAuthReturnAction.ChannelSetup)
                            ),
                    };
                }
            )
            .RequireAuthorization();
    }

    private static async Task<IResult> CompleteBroadcasterAuthorizationAsync(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        HostBotAccountOAuthService oauth,
        HostBroadcasterAuthorizationService authorization,
        HostBroadcasterOAuthStateStore states,
        ILogger<BotOAuthEndpointLog> logger,
        CancellationToken ct
    )
    {
        var session = AuthenticatedSession.FromPrincipal(context.User);
        if (!states.TryConsume(state, session.UserId, out var hostId))
        {
            return InvalidOrExpired(
                BlokeBotAuthRetryAction.Broadcaster,
                BlokeBotAuthReturnAction.ChannelSetup
            );
        }

        var selectedHost = SelectedHost(session);
        if (selectedHost?.Id != hostId || !session.CanAuthorizeSelectedHost)
        {
            return ConnectionAccessResult(session);
        }

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            return InvalidOrExpired(
                BlokeBotAuthRetryAction.Broadcaster,
                BlokeBotAuthReturnAction.ChannelSetup
            );
        }
        try
        {
            var completion = await oauth.CompleteAsync(code, ct);
            return await completion.Match<Task<IResult>>(
                async completed =>
                    (await authorization.AuthorizeAsync(hostId, completed.Grant, ct))
                    is HostBroadcasterAuthorizationOutcome.Authorized
                        ? Success(
                            BlokeBotAuthReturnAction.ChannelSetup,
                            BlokeBotAuthSuccessKind.ChannelConnection
                        )
                        : PermissionOrAccount(BlokeBotAuthRetryAction.Broadcaster),
                _ => Task.FromResult<IResult>(Unavailable(BlokeBotAuthReturnAction.ChannelSetup)),
                _ =>
                    Task.FromResult<IResult>(
                        PermissionOrAccount(BlokeBotAuthRetryAction.Broadcaster)
                    )
            );
        }
        catch (HttpRequestException exception)
        {
            return ProviderFailureResult(
                BlokeBotAuthRetryAction.Broadcaster,
                context,
                logger,
                "TransportFailure",
                exception.GetType().Name
            );
        }
    }

    private static async Task<IResult> CompleteHostBotAuthorizationAsync(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        HostBotAccountOAuthService oauth,
        HostBotAccountAuthorizationService hostBotAuthorization,
        HostBotOAuthStateStore hostBotStates,
        ILogger<BotOAuthEndpointLog> logger,
        CancellationToken ct
    )
    {
        var session = AuthenticatedSession.FromPrincipal(context.User);
        var stateConsumption = hostBotStates.Consume(state, session.UserId);
        if (stateConsumption is not HostBotOAuthStateConsumption.Consumed consumed)
        {
            return InvalidOrExpired(
                BlokeBotAuthRetryAction.HostBot,
                BlokeBotAuthReturnAction.ChannelSetup
            );
        }

        var selectedHost = SelectedHost(session);
        if (selectedHost?.Id != consumed.HostId || !session.CanAuthorizeSelectedHostBotAccount)
        {
            return ConnectionAccessResult(session);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return ProviderErrorResult(error, BlokeBotAuthRetryAction.HostBot, context, logger);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Cancelled(
                BlokeBotAuthRetryAction.HostBot,
                BlokeBotAuthReturnAction.ChannelSetup
            );
        }

        try
        {
            var completion = await oauth.CompleteAsync(code, ct);
            return await completion.Match<Task<IResult>>(
                async completed =>
                {
                    var actor = HostBotAccountActorFor(session);
                    var authorization = await hostBotAuthorization
                        .Authorize(consumed.HostId, actor, completed.Grant)
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
    ) =>
        outcome switch
        {
            ChannelBotAuthorizationOutcome.Authorized => Success(
                BlokeBotAuthReturnAction.ChannelSetup,
                BlokeBotAuthSuccessKind.ChannelConnection
            ),
            ChannelBotAuthorizationOutcome.HostNotFound => NoChannelSelected(),
            ChannelBotAuthorizationOutcome.GrantMismatch => Result(
                BlokeBotAuthOutcome.WrongAccount,
                BlokeBotAuthStatus.BadRequest,
                BlokeBotAuthRetryAction.ChannelBot,
                BlokeBotAuthReturnAction.ChannelSetup,
                resultContext: new BlokeBotAuthContext.RequiredChannel(requiredChannelLogin)
            ),
            ChannelBotAuthorizationOutcome.MissingScopes => PermissionOrAccount(
                BlokeBotAuthRetryAction.ChannelBot
            ),
            _ => throw new UnreachableException(),
        };

    private static IResult MapHostBotAuthorization(HostBotAccountAuthorizationOutcome outcome) =>
        outcome switch
        {
            HostBotAccountAuthorizationOutcome.Authorized => Success(
                BlokeBotAuthReturnAction.ChannelSetup,
                BlokeBotAuthSuccessKind.ChannelConnection
            ),
            HostBotAccountAuthorizationOutcome.HostNotFound => NoChannelSelected(),
            HostBotAccountAuthorizationOutcome.OverrideDisabled => CustomBotDisabled(),
            HostBotAccountAuthorizationOutcome.AuthorityDenied => AccessRequired(
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            HostBotAccountAuthorizationOutcome.MissingScopes => PermissionOrAccount(
                BlokeBotAuthRetryAction.HostBot
            ),
            HostBotAccountAuthorizationOutcome.ProtectionUnavailable => Unavailable(
                BlokeBotAuthReturnAction.ChannelSetup
            ),
            _ => throw new UnreachableException(),
        };

    private static BotHostChoice? SelectedHost(AuthenticatedSession session) =>
        session.State.Match<BotHostChoice?>(
            _ => null,
            selected => selected.Selection.Current,
            _ => null
        );

    private static IResult ConnectionAccessResult(AuthenticatedSession session) =>
        session.State.Match<IResult>(
            static _ => NoChannelSelected(),
            static _ => AccessRequired(BlokeBotAuthReturnAction.ChannelSetup),
            static _ => NoChannelSelected()
        );

    private static IResult ProviderErrorResult(
        string error,
        BlokeBotAuthRetryAction retryAction,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger
    ) =>
        string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
            ? Cancelled(retryAction, BlokeBotAuthReturnAction.ChannelSetup)
            : ProviderFailureResult(
                retryAction,
                context,
                logger,
                "OAuthErrorQuery",
                "OAuthErrorQuery"
            );

    private static HostBotAccountActor HostBotAccountActorFor(AuthenticatedSession session) =>
        session.CurrentHostRoleIs(AuthRole.Admin)
            ? new HostBotAccountActor.BotAdministrator(session.UserId, session.Login)
            : new HostBotAccountActor.ChannelOwner(session.UserId, session.Login);

    private static IResult BotAccountProviderErrorResult(
        string error,
        HttpContext context,
        ILogger<BotOAuthEndpointLog> logger
    ) =>
        string.Equals(error, "access_denied", StringComparison.OrdinalIgnoreCase)
            ? Cancelled(BlokeBotAuthRetryAction.BotAccount, BlokeBotAuthReturnAction.Admin)
            : BotAccountProviderFailureResult(
                context,
                logger,
                "OAuthErrorQuery",
                "OAuthErrorQuery"
            );

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
    ) =>
        logger.LogWarning(
            "Twitch bot OAuth failed; Classification: {Classification}; FailureType: {FailureType}; SupportReference: {SupportReference}.",
            classification,
            failureType,
            supportReference
        );

    private static BlokeBotAuthResult Unavailable(BlokeBotAuthReturnAction returnAction) =>
        Result(
            BlokeBotAuthOutcome.Unavailable,
            BlokeBotAuthStatus.ServiceUnavailable,
            BlokeBotAuthRetryAction.None,
            returnAction
        );

    private static BlokeBotAuthResult NoChannelSelected() =>
        Result(
            BlokeBotAuthOutcome.NoChannelSelected,
            BlokeBotAuthStatus.Forbidden,
            BlokeBotAuthRetryAction.None,
            BlokeBotAuthReturnAction.ChannelSetup
        );

    private static BlokeBotAuthResult AccessRequired(BlokeBotAuthReturnAction returnAction) =>
        Result(
            BlokeBotAuthOutcome.AccessRequired,
            BlokeBotAuthStatus.Forbidden,
            BlokeBotAuthRetryAction.None,
            returnAction
        );

    private static BlokeBotAuthResult Cancelled(
        BlokeBotAuthRetryAction retryAction,
        BlokeBotAuthReturnAction returnAction
    ) =>
        Result(
            BlokeBotAuthOutcome.Cancelled,
            BlokeBotAuthStatus.BadRequest,
            retryAction,
            returnAction
        );

    private static BlokeBotAuthResult InvalidOrExpired(
        BlokeBotAuthRetryAction retryAction,
        BlokeBotAuthReturnAction returnAction
    ) =>
        Result(
            BlokeBotAuthOutcome.InvalidOrExpired,
            BlokeBotAuthStatus.BadRequest,
            retryAction,
            returnAction
        );

    private static BlokeBotAuthResult PermissionOrAccount(BlokeBotAuthRetryAction retryAction) =>
        Result(
            BlokeBotAuthOutcome.PermissionOrAccount,
            BlokeBotAuthStatus.BadRequest,
            retryAction,
            BlokeBotAuthReturnAction.ChannelSetup
        );

    private static BlokeBotAuthResult CustomBotDisabled() =>
        Result(
            BlokeBotAuthOutcome.CustomBotDisabled,
            BlokeBotAuthStatus.BadRequest,
            BlokeBotAuthRetryAction.None,
            BlokeBotAuthReturnAction.ChannelSetup
        );

    private static BlokeBotAuthResult Success(
        BlokeBotAuthReturnAction returnAction,
        BlokeBotAuthSuccessKind successKind
    ) =>
        Result(
            BlokeBotAuthOutcome.Success,
            BlokeBotAuthStatus.Ok,
            BlokeBotAuthRetryAction.None,
            returnAction,
            resultContext: new BlokeBotAuthContext.Success(successKind)
        );

    private static BlokeBotAuthResult Result(
        BlokeBotAuthOutcome outcome,
        BlokeBotAuthStatus status,
        BlokeBotAuthRetryAction retryAction,
        BlokeBotAuthReturnAction returnAction,
        string? supportReference = null,
        BlokeBotAuthContext? resultContext = null
    ) => new(outcome, status, retryAction, returnAction, supportReference, resultContext);

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

    private sealed class BotOAuthEndpointLog;
}
