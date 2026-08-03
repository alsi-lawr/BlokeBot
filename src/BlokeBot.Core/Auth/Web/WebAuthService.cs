using System.Text.Json;
using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Users;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Hosts;
using BlokeBot.Core.Identity;
using BlokeBot.Functional;

namespace BlokeBot.Core.Auth.Web;

internal sealed class WebAuthService(
    WebAuthConfiguration configuration,
    WebOAuthClient oauth,
    UserLookupService users,
    BotAdminService admins,
    BotSettings botSettings,
    AuthorizedHostSelectionService hosts
)
{
    public WebAuthOptions CurrentOptions => configuration.CurrentOptions;

    public Uri CreateAuthorizationUri(HttpRequest request, string state) =>
        oauth.CreateAuthorizationUri(request, CurrentOptions, state);

    public IO<WebAuthenticationOutcome, WebAuthenticationError> Authenticate(
        HttpRequest request,
        string code
    ) =>
        IO<WebAuthenticationOutcome, WebAuthenticationError>.Create(async ct =>
        {
            var currentOptions = CurrentOptions;
            if (!IsConfigured(currentOptions))
            {
                return Success(new WebAuthenticationOutcome.NotConfigured());
            }

            string accessToken;
            try
            {
                accessToken = await oauth.ExchangeCodeAsync(request, currentOptions, code, ct);
            }
            catch (HttpRequestException exception)
            {
                ct.ThrowIfCancellationRequested();
                return Error(WebAuthenticationError.TransportFailure.From(exception));
            }
            catch (JsonException exception)
            {
                ct.ThrowIfCancellationRequested();
                return Error(WebAuthenticationError.InvalidProviderPayload.From(exception));
            }
            catch (InvalidOperationException exception)
            {
                ct.ThrowIfCancellationRequested();
                return Error(WebAuthenticationError.InvalidProviderPayload.From(exception));
            }

            try
            {
                var user = await users.GetCurrentUserAsync(accessToken, ct);
                return await user.Match(
                    identity => AuthorizeIdentityAsync(accessToken, identity, ct),
                    () => Task.FromResult(Success(new WebAuthenticationOutcome.UserNotValidated()))
                );
            }
            catch (HttpRequestException exception)
            {
                ct.ThrowIfCancellationRequested();
                return Error(WebAuthenticationError.TransportFailure.From(exception));
            }
            catch (JsonException exception)
            {
                ct.ThrowIfCancellationRequested();
                return Error(WebAuthenticationError.InvalidProviderPayload.From(exception));
            }
        });

    private async Task<
        Result<WebAuthenticationOutcome, WebAuthenticationError>
    > AuthorizeIdentityAsync(
        string accessToken,
        UserIdentity user,
        CancellationToken cancellationToken
    )
    {
        var twitchUserId = user.Id;
        var twitchLogin = user.Login;
        var userLogin = LoginName.Parse(twitchLogin).Value;
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? twitchLogin
            : user.DisplayName;
        if (IsConfiguredBotAccount(userLogin))
        {
            return Success(
                new WebAuthenticationOutcome.Authorized(
                    new AuthenticatedUser(
                        twitchUserId,
                        twitchLogin,
                        displayName,
                        user.ProfileImageUrl,
                        [],
                        false
                    )
                )
            );
        }

        var authorizedHosts = await hosts.LoadAuthorizedHostsAsync(
            accessToken,
            twitchUserId,
            userLogin,
            cancellationToken
        );

        return
            authorizedHosts.Choices.Count == 0
            && !authorizedHosts.CanCreateHost
            && !admins.IsAdmin(userLogin)
            ? Success(
                new WebAuthenticationOutcome.NotAuthorized(
                    "This Twitch account cannot create or manage a BlokeBot channel yet."
                )
            )
            : Success(
                new WebAuthenticationOutcome.Authorized(
                    new AuthenticatedUser(
                        twitchUserId,
                        twitchLogin,
                        displayName,
                        user.ProfileImageUrl,
                        authorizedHosts.Choices,
                        authorizedHosts.CanCreateHost
                    )
                )
            );
    }

    public bool IsConfigured(WebAuthOptions currentOptions) =>
        configuration.IsConfigured(currentOptions);

    private bool IsConfiguredBotAccount(string login) =>
        !string.IsNullOrWhiteSpace(botSettings.Identity.BotUsername)
        && string.Equals(
            Login.Normalize(login),
            botSettings.Identity.BotUsername,
            StringComparison.Ordinal
        );

    private static Result<WebAuthenticationOutcome, WebAuthenticationError> Success(
        WebAuthenticationOutcome outcome
    ) => Result<WebAuthenticationOutcome, WebAuthenticationError>.Success(outcome);

    private static Result<WebAuthenticationOutcome, WebAuthenticationError> Error(
        WebAuthenticationError error
    ) => Result<WebAuthenticationOutcome, WebAuthenticationError>.Error(error);
}

internal abstract record WebAuthenticationOutcome
{
    private WebAuthenticationOutcome() { }

    internal sealed record Authorized(AuthenticatedUser User) : WebAuthenticationOutcome;

    internal sealed record NotConfigured : WebAuthenticationOutcome;

    internal sealed record UserNotValidated : WebAuthenticationOutcome;

    internal sealed record NotAuthorized(string Message) : WebAuthenticationOutcome;
}

internal abstract record WebAuthenticationError
{
    private WebAuthenticationError() { }

    internal sealed record TransportFailure(string FailureType) : WebAuthenticationError
    {
        public static TransportFailure From(HttpRequestException exception) =>
            new(exception.GetType().Name);
    }

    internal sealed record InvalidProviderPayload(string FailureType) : WebAuthenticationError
    {
        public static InvalidProviderPayload From(Exception exception) =>
            new(exception.GetType().Name);
    }
}
