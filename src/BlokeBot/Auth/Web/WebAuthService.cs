using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Users;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Hosts;
using BlokeBot.Identity;

namespace BlokeBot.Auth.Web;

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

    public Uri CreateAuthorizationUri(HttpRequest request, string state)
    {
        return oauth.CreateAuthorizationUri(request, CurrentOptions, state);
    }

    public async Task<AuthResult> AuthenticateAsync(
        HttpRequest request,
        string code,
        CancellationToken cancellationToken
    )
    {
        var currentOptions = CurrentOptions;
        if (!IsConfigured(currentOptions))
        {
            return new AuthResult(false, null, "Twitch sign-in is not set up yet.");
        }

        var accessToken = await oauth.ExchangeCodeAsync(
            request,
            currentOptions,
            code,
            cancellationToken
        );
        var user = await users.GetCurrentUserAsync(currentOptions, accessToken, cancellationToken);

        return await user.Match(
            identity => AuthenticateAsync(currentOptions, accessToken, identity, cancellationToken),
            () =>
                Task.FromResult(
                    new AuthResult(false, null, "Twitch did not return the signed-in user.")
                )
        );
    }

    private async Task<AuthResult> AuthenticateAsync(
        WebAuthOptions currentOptions,
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
            return new AuthResult(
                true,
                new AuthenticatedUser(
                    twitchUserId,
                    twitchLogin,
                    displayName,
                    user.ProfileImageUrl,
                    [],
                    false
                ),
                null
            );
        }

        var authorizedHosts = await hosts.LoadAuthorizedHostsAsync(
            currentOptions,
            accessToken,
            twitchUserId,
            userLogin,
            cancellationToken
        );

        if (
            authorizedHosts.Choices.Count == 0
            && !authorizedHosts.CanCreateHost
            && !admins.IsAdmin(userLogin)
        )
        {
            return new AuthResult(
                false,
                null,
                "This Twitch account cannot create or manage a BlokeBot channel yet."
            );
        }

        return new AuthResult(
            true,
            new AuthenticatedUser(
                twitchUserId,
                twitchLogin,
                displayName,
                user.ProfileImageUrl,
                authorizedHosts.Choices,
                authorizedHosts.CanCreateHost
            ),
            null
        );
    }

    public bool IsConfigured(WebAuthOptions currentOptions)
    {
        return configuration.IsConfigured(currentOptions);
    }

    private bool IsConfiguredBotAccount(string login)
    {
        return !string.IsNullOrWhiteSpace(botSettings.Identity.BotUsername)
            && string.Equals(
                Login.Normalize(login),
                botSettings.Identity.BotUsername,
                StringComparison.Ordinal
            );
    }
}
