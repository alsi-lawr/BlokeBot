using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

internal sealed class ConfiguredBotAccountAuthorizationPolicy(
    BotSettings settings,
    IAccessTokenCache tokenCache,
    HelixClient helix,
    ITokenStatusSource tokenStatus,
    HostedChannelChangeNotifier changes
) : IBotAccountAuthorizationPolicy
{
    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken ct)
    {
        var inspection = await tokenStatus
            .GetUserAccessTokenStatus(settings.Identity.Scopes)
            .ExecuteAsync(ct);
        var status = inspection.Match<TokenStatus>(
            value => value,
            error => new TokenStatus.Unknown(error)
        );
        var configuredBotLogin = settings.Identity.BotUsername;

        return await status.Match(
            unknown => Task.FromResult(Unknown(configuredBotLogin, unknown)),
            unavailable => Task.FromResult(NotAuthorized(configuredBotLogin, unavailable)),
            invalid => Task.FromResult(NotAuthorized(configuredBotLogin, invalid)),
            missingScopes =>
                GetAuthorizedStatusAsync(
                    configuredBotLogin,
                    missingScopes.AccessToken,
                    missingScopes.Validation,
                    missingScopes.RequiredScopes,
                    missingScopes.GrantedScopes,
                    missingScopes.Missing,
                    ct
                ),
            ready =>
                GetAuthorizedStatusAsync(
                    configuredBotLogin,
                    ready.AccessToken,
                    ready.Validation,
                    ready.RequiredScopes,
                    ready.GrantedScopes,
                    [],
                    ct
                )
        );
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        var tokenCachePath = settings.Identity.TokenCachePath;
        if (!string.IsNullOrWhiteSpace(tokenCachePath) && File.Exists(tokenCachePath))
        {
            File.Delete(tokenCachePath);
        }

        await tokenCache.ClearAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    private async Task<BotAccountAuthorizationStatus> GetAuthorizedStatusAsync(
        string configuredBotLogin,
        string accessToken,
        TokenValidation validation,
        ImmutableArray<string> requiredScopes,
        ImmutableArray<string> grantedScopes,
        ImmutableArray<string> missingScopes,
        CancellationToken ct
    )
    {
        var authorizedLogin = LoginName.Parse(validation.Login).Value;
        var authorizedProfileImageUrl = await LoadAuthorizedProfileImageUrlAsync(accessToken, ct);
        if (!string.Equals(configuredBotLogin, authorizedLogin, StringComparison.Ordinal))
        {
            return new(
                configuredBotLogin,
                authorizedLogin,
                authorizedProfileImageUrl,
                BotAccountAuthorizationState.WrongAccount,
                requiredScopes,
                grantedScopes,
                missingScopes,
                "The connected Twitch account is not the expected bot account."
            );
        }

        return missingScopes.IsEmpty
            ? new(
                configuredBotLogin,
                authorizedLogin,
                authorizedProfileImageUrl,
                BotAccountAuthorizationState.Ready,
                requiredScopes,
                grantedScopes,
                [],
                "The bot account is ready."
            )
            : new(
                configuredBotLogin,
                authorizedLogin,
                authorizedProfileImageUrl,
                BotAccountAuthorizationState.MissingScopes,
                requiredScopes,
                grantedScopes,
                missingScopes,
                "The bot account needs more Twitch access."
            );
    }

    private async Task<string?> LoadAuthorizedProfileImageUrlAsync(
        string accessToken,
        CancellationToken ct
    )
    {
        var user = await helix.GetCurrentUserAsync(
            new HelixRequestContext(settings.Identity.ClientId, accessToken),
            ct
        );

        return string.IsNullOrWhiteSpace(user?.ProfileImageUrl) ? null : user.ProfileImageUrl;
    }

    private static BotAccountAuthorizationStatus Unknown(
        string configuredBotLogin,
        TokenStatus.Unknown status
    )
    {
        return new(
            configuredBotLogin,
            null,
            null,
            BotAccountAuthorizationState.Unknown,
            status.Error.RequiredScopes,
            [],
            status.Error.RequiredScopes,
            "BlokeBot could not check the bot account right now."
        );
    }

    private static BotAccountAuthorizationStatus NotAuthorized(
        string configuredBotLogin,
        TokenStatus.Unavailable status
    )
    {
        return new(
            configuredBotLogin,
            null,
            null,
            BotAccountAuthorizationState.NotAuthorized,
            status.RequiredScopes,
            [],
            status.RequiredScopes,
            "No bot account is connected yet."
        );
    }

    private static BotAccountAuthorizationStatus NotAuthorized(
        string configuredBotLogin,
        TokenStatus.Invalid status
    )
    {
        return new(
            configuredBotLogin,
            null,
            null,
            BotAccountAuthorizationState.NotAuthorized,
            status.RequiredScopes,
            [],
            status.RequiredScopes,
            "BlokeBot could not check the connected bot account."
        );
    }
}
