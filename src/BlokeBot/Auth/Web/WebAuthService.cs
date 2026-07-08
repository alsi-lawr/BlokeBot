using Alsi.TwitchBot;
using BlokeBot.Auth.Hosts;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Users;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Identity;
using Microsoft.Extensions.Options;

namespace BlokeBot.Auth.Web;

internal sealed class WebAuthService(
    WebAuthConfiguration configuration,
    WebOAuthClient oauth,
    UserLookupService users,
    BotAdminService admins,
    IOptions<TwitchBotOptions> botOptions,
    AuthorizedHostResolver hosts
)
{
    public WebAuthOptions CurrentOptions => configuration.CurrentOptions;

    public Uri CreateAuthorizationUri(HttpRequest request, string state) =>
        oauth.CreateAuthorizationUri(request, CurrentOptions, state);

    public async Task<AuthResult> AuthenticateAsync(
        HttpRequest request,
        string code,
        CancellationToken cancellationToken
    )
    {
        var currentOptions = CurrentOptions;
        if (!IsConfigured(currentOptions))
            return new AuthResult(false, null, "Twitch web authentication is incomplete.");

        var accessToken = await oauth.ExchangeCodeAsync(
            request,
            currentOptions,
            code,
            cancellationToken
        );
        var twitchUser = await users.GetCurrentUserAsync(
            currentOptions,
            accessToken,
            cancellationToken
        );
        if (twitchUser is null)
            return new AuthResult(false, null, "Twitch did not return the signed-in user.");

        var twitchUserId = twitchUser.Id!;
        var twitchLogin = twitchUser.Login!;
        var userLogin = LoginName.Parse(twitchLogin).Value;
        var displayName = string.IsNullOrWhiteSpace(twitchUser.DisplayName)
            ? twitchLogin
            : twitchUser.DisplayName;
        if (IsConfiguredBotAccount(userLogin))
        {
            return new AuthResult(
                true,
                new AuthenticatedUser(
                    twitchUserId,
                    twitchLogin,
                    displayName,
                    twitchUser.ProfileImageUrl,
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
            displayName,
            twitchUser.ProfileImageUrl,
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
                "This Twitch account cannot create or manage any hosted BlokeBot channel yet."
            );
        }

        return new AuthResult(
            true,
            new AuthenticatedUser(
                twitchUserId,
                twitchLogin,
                displayName,
                twitchUser.ProfileImageUrl,
                authorizedHosts.Choices,
                authorizedHosts.CanCreateHost
            ),
            null
        );
    }

    public bool IsConfigured(WebAuthOptions currentOptions) =>
        configuration.IsConfigured(currentOptions);

    private bool IsConfiguredBotAccount(string login) =>
        !string.IsNullOrWhiteSpace(botOptions.Value.Identity.BotUsername)
        && string.Equals(
            NormalizeLogin(login),
            NormalizeLogin(botOptions.Value.Identity.BotUsername),
            StringComparison.Ordinal
        );

    private static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('#').ToLowerInvariant();
}
