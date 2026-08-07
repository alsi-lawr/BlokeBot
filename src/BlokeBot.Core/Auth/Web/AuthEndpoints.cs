using System.Diagnostics;
using System.Security.Cryptography;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Hosts;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Auth.Web;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        _ = app.MapGet(
                "/auth/login",
                (
                    HttpContext context,
                    WebAuthService auth,
                    IOptions<PrivacyNoticeOptions> privacy,
                    bool? start,
                    string? returnUrl
                ) =>
                {
                    var action =
                        start == true
                            ? (LoginAction)new LoginAction.StartOAuth()
                            : new LoginAction.ShowLoginPage();
                    var currentOptions = auth.CurrentOptions;
                    return !auth.IsConfigured(currentOptions)
                        ? Result(
                            BlokeBotAuthOutcome.Unavailable,
                            BlokeBotAuthStatus.ServiceUnavailable,
                            BlokeBotAuthRetryAction.None,
                            BlokeBotAuthReturnAction.SignIn
                        )
                        : action.Match<IResult>(
                            _ =>
                                Results.Content(
                                    LoginPage.Render(privacy.Value.NoticeUri?.ToString()),
                                    "text/html"
                                ),
                            _ => StartLogin()
                        );
                    IResult StartLogin()
                    {
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
                }
            )
            .AllowAnonymous();

        _ = app.MapGet(
                "/auth/twitch/callback",
                async (
                    HttpContext context,
                    string? code,
                    string? state,
                    string? error,
                    WebAuthService auth,
                    AuthSessionService session,
                    ILogger<WebAuthEndpointLog> logger,
                    CancellationToken cancellationToken
                ) =>
                {
                    var storedState = context.Request.Cookies["BlokeBot.AuthState"];
                    var returnUrl = context.Request.Cookies["BlokeBot.AuthReturnUrl"];
                    context.Response.Cookies.Delete("BlokeBot.AuthState");
                    context.Response.Cookies.Delete("BlokeBot.AuthReturnUrl");

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return string.Equals(
                            error,
                            "access_denied",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? Result(
                                BlokeBotAuthOutcome.Cancelled,
                                BlokeBotAuthStatus.BadRequest,
                                BlokeBotAuthRetryAction.SignIn,
                                BlokeBotAuthReturnAction.SignIn
                            )
                            : OAuthErrorFailure(context, logger);
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return Result(
                            BlokeBotAuthOutcome.Cancelled,
                            BlokeBotAuthStatus.BadRequest,
                            BlokeBotAuthRetryAction.SignIn,
                            BlokeBotAuthReturnAction.SignIn
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
                            BlokeBotAuthRetryAction.SignIn,
                            BlokeBotAuthReturnAction.SignIn
                        );
                    }

                    var result = await auth.Authenticate(context.Request, code)
                        .ExecuteAsync(cancellationToken);
                    return await result.Match(
                        outcome =>
                            CompleteAuthenticationAsync(outcome, context, session, returnUrl),
                        error => Task.FromResult(MapAuthenticationError(error, context, logger))
                    );
                }
            )
            .AllowAnonymous();

        static async Task<IResult> CompleteAuthenticationAsync(
            WebAuthenticationOutcome outcome,
            HttpContext context,
            AuthSessionService session,
            string? returnUrl
        )
        {
            if (outcome is not WebAuthenticationOutcome.Authorized authorized)
            {
                return outcome switch
                {
                    WebAuthenticationOutcome.NotConfigured => Result(
                        BlokeBotAuthOutcome.Unavailable,
                        BlokeBotAuthStatus.Forbidden,
                        BlokeBotAuthRetryAction.None,
                        BlokeBotAuthReturnAction.SignIn
                    ),
                    WebAuthenticationOutcome.UserNotValidated => Result(
                        BlokeBotAuthOutcome.AccessRequired,
                        BlokeBotAuthStatus.Forbidden,
                        BlokeBotAuthRetryAction.SignIn,
                        BlokeBotAuthReturnAction.SignIn
                    ),
                    WebAuthenticationOutcome.NotAuthorized => Result(
                        BlokeBotAuthOutcome.AccessRequired,
                        BlokeBotAuthStatus.Forbidden,
                        BlokeBotAuthRetryAction.SignIn,
                        BlokeBotAuthReturnAction.SignIn
                    ),
                    _ => throw new UnreachableException(),
                };
            }

            var currentSession = AuthenticatedSession.FromPrincipal(context.User);
            await session.SignInAsync(
                context,
                authorized.User,
                currentSession.State.Match<int?>(
                    _ => null,
                    selected => selected.Selection.Current.Id,
                    _ => null
                )
            );
            return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/"));
        }

        _ = app.MapGet(
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
                    {
                        return Results.Redirect("/auth/login");
                    }

                    var selected = available.FirstOrDefault(host => host.Id == hostId);
                    if (selected is null)
                    {
                        return Results.Forbid();
                    }

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

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/"));
                }
            )
            .RequireAuthorization();

        _ = app.MapGet(
                "/auth/select-own-host",
                async (HttpContext context, string? returnUrl, AuthSessionService session) =>
                {
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    if (currentSession.IsBotAccount)
                    {
                        return Results.Forbid();
                    }

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

                        return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/"));
                    }

                    if (!currentSession.CanCreateHost)
                    {
                        return Results.Forbid();
                    }

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

        _ = app.MapGet(
                "/auth/recover-moderator-access",
                async (
                    HttpContext context,
                    int hostId,
                    string? returnUrl,
                    AuthSessionService session
                ) =>
                {
                    var currentSession = AuthenticatedSession.FromPrincipal(context.User);
                    var recovery = ClearRevokedModeratorHost(currentSession, hostId);
                    await session.SignInHostSelectionAsync(
                        context,
                        recovery.Hosts,
                        recovery.SelectedHost,
                        currentSession.IsBotAdmin,
                        currentSession.AdminEditingLogin
                    );
                    return Results.Redirect(
                        $"/auth/login?start=true&returnUrl={Uri.EscapeDataString(LocalReturnUrl.OrFallback(returnUrl, "/"))}"
                    );
                }
            )
            .RequireAuthorization();

        _ = app.MapGet(
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
                    {
                        return Results.Forbid();
                    }

                    var selectedResult = await hostedChannels
                        .LoadHostChoice(hostId, AuthRole.Admin)
                        .ExecuteAsync(context.RequestAborted);
                    var selected = selectedResult.Match(
                        choice => choice.Match<BotHostChoice?>(value => value, () => null),
                        _ => throw new UnreachableException()
                    );
                    if (selected is null)
                    {
                        return Results.NotFound();
                    }

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

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/"));
                }
            )
            .RequireAuthorization("BotAdmin");

        _ = app.MapGet(
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
                    {
                        nonAdminHosts = [.. nonAdminHosts, returnHost];
                    }

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

                    return Results.Redirect(LocalReturnUrl.OrFallback(returnUrl, "/"));
                }
            )
            .RequireAuthorization("BotAdmin");

        _ = app.MapGet(
                "/auth/logout",
                async (HttpContext context, AuthSessionService session) =>
                {
                    await session.SignOutAsync(context);
                    return Results.Redirect("/auth/login");
                }
            )
            .AllowAnonymous();
    }

    internal static RecoveredModeratorHostSelection ClearRevokedModeratorHost(
        AuthenticatedSession session,
        int hostId
    ) =>
        new(
            session
                .AvailableHosts.Where(host => host.Id != hostId || host.Role != AuthRole.Moderator)
                .ToArray(),
            null
        );

    internal static IResult MapAuthenticationError(
        WebAuthenticationError error,
        HttpContext context,
        ILogger<WebAuthEndpointLog> logger
    ) =>
        error switch
        {
            WebAuthenticationError.TransportFailure transport => ProviderFailure(
                context,
                logger,
                "TransportFailure",
                transport.FailureType
            ),
            WebAuthenticationError.InvalidProviderPayload invalid => ProviderFailure(
                context,
                logger,
                "InvalidProviderPayload",
                invalid.FailureType
            ),
            _ => throw new UnreachableException(),
        };

    internal static IResult ProviderFailure(
        HttpContext context,
        ILogger<WebAuthEndpointLog> logger,
        string classification,
        string failureType
    )
    {
        logger.LogWarning(
            "Twitch sign-in failed; Classification: {Classification}; FailureType: {FailureType}; SupportReference: {SupportReference}.",
            classification,
            failureType,
            context.TraceIdentifier
        );
        return Result(
            BlokeBotAuthOutcome.ProviderUnavailable,
            BlokeBotAuthStatus.BadGateway,
            BlokeBotAuthRetryAction.SignIn,
            BlokeBotAuthReturnAction.SignIn,
            context.TraceIdentifier
        );
    }

    internal static IResult OAuthErrorFailure(
        HttpContext context,
        ILogger<WebAuthEndpointLog> logger
    )
    {
        logger.LogWarning(
            "Twitch sign-in failed; Classification: {Classification}; FailureType: {FailureType}; SupportReference: {SupportReference}.",
            "OAuthErrorQuery",
            "OAuthErrorQuery",
            context.TraceIdentifier
        );
        return Result(
            BlokeBotAuthOutcome.ProviderUnavailable,
            BlokeBotAuthStatus.BadRequest,
            BlokeBotAuthRetryAction.SignIn,
            BlokeBotAuthReturnAction.SignIn,
            context.TraceIdentifier
        );
    }

    private static BlokeBotAuthResult Result(
        BlokeBotAuthOutcome outcome,
        BlokeBotAuthStatus status,
        BlokeBotAuthRetryAction retryAction,
        BlokeBotAuthReturnAction returnAction,
        string? supportReference = null,
        BlokeBotAuthContext? resultContext = null
    ) => new(outcome, status, retryAction, returnAction, supportReference, resultContext);

    private abstract record LoginAction
    {
        private LoginAction() { }

        internal abstract TResult Match<TResult>(
            Func<ShowLoginPage, TResult> showLoginPage,
            Func<StartOAuth, TResult> startOAuth
        );

        internal sealed record ShowLoginPage : LoginAction
        {
            internal override TResult Match<TResult>(
                Func<ShowLoginPage, TResult> showLoginPage,
                Func<StartOAuth, TResult> startOAuth
            ) => showLoginPage(this);
        }

        internal sealed record StartOAuth : LoginAction
        {
            internal override TResult Match<TResult>(
                Func<ShowLoginPage, TResult> showLoginPage,
                Func<StartOAuth, TResult> startOAuth
            ) => startOAuth(this);
        }
    }
}

internal sealed record RecoveredModeratorHostSelection(
    IReadOnlyList<BotHostChoice> Hosts,
    BotHostChoice? SelectedHost
);

internal sealed class WebAuthEndpointLog;
