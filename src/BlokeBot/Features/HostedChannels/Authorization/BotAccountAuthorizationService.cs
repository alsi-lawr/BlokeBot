using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Authorization;

public enum BotAccountAuthorizationState
{
    Unknown,
    NotAuthorized,
    WrongAccount,
    MissingScopes,
    Ready,
}

public sealed record BotAccountAuthorizationStatus(
    string ConfiguredBotLogin,
    string? AuthorizedLogin,
    BotAccountAuthorizationState State,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<string> MissingScopes,
    string Message
);

public sealed class BotAccountAuthorizationService(
    IOptions<TwitchBotOptions> options,
    IServiceProvider services,
    TwitchOAuthApiClient oauth,
    HostedChannelChangeNotifier changes
)
{
    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken ct)
    {
        var configuredBotLogin = LoginName.Parse(options.Value.Identity.BotUsername).Value;
        var requiredScopes = TwitchScopeSet.NormalizeMany(options.Value.Identity.Scopes);

        string token;
        try
        {
            token = await GetUserAccessTokenAsync(ct);
        }
        catch (InvalidOperationException)
        {
            return new(
                configuredBotLogin,
                null,
                BotAccountAuthorizationState.NotAuthorized,
                requiredScopes,
                [],
                requiredScopes,
                "Bot account authorization has not been completed."
            );
        }

        try
        {
            var validation = await oauth.ValidateTokenAsync(token, ct);
            if (validation is null)
            {
                return new(
                    configuredBotLogin,
                    null,
                    BotAccountAuthorizationState.NotAuthorized,
                    requiredScopes,
                    [],
                    requiredScopes,
                    "Bot account authorization could not be verified."
                );
            }

            var grantedScopes = validation.Scopes.Order(StringComparer.Ordinal).ToArray();
            var missingScopes = TwitchScopeSet.Missing(grantedScopes, requiredScopes);
            var authorizedLogin = LoginName.Parse(validation.Login).Value;
            if (!string.Equals(configuredBotLogin, authorizedLogin, StringComparison.Ordinal))
            {
                return new(
                    configuredBotLogin,
                    authorizedLogin,
                    BotAccountAuthorizationState.WrongAccount,
                    requiredScopes,
                    grantedScopes,
                    missingScopes,
                    "The saved authorization belongs to a different Twitch account."
                );
            }

            if (missingScopes.Length > 0)
            {
                return new(
                    configuredBotLogin,
                    authorizedLogin,
                    BotAccountAuthorizationState.MissingScopes,
                    requiredScopes,
                    grantedScopes,
                    missingScopes,
                    "Bot account authorization is missing configured permissions."
                );
            }

            return new(
                configuredBotLogin,
                authorizedLogin,
                BotAccountAuthorizationState.Ready,
                requiredScopes,
                grantedScopes,
                [],
                "Bot account authorization is current."
            );
        }
        catch
        {
            return new(
                configuredBotLogin,
                null,
                BotAccountAuthorizationState.Unknown,
                requiredScopes,
                [],
                requiredScopes,
                "Bot account authorization status could not be checked."
            );
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        var tokenCachePath = options.Value.Identity.TokenCachePath;
        if (!string.IsNullOrWhiteSpace(tokenCachePath) && File.Exists(tokenCachePath))
            File.Delete(tokenCachePath);

        var tokenCache = services.GetService<ITwitchAccessTokenCache>();
        if (tokenCache is not null)
            await tokenCache.ClearAsync(ct);

        await changes.NotifyChangedAsync();
    }

    private async Task<string> GetUserAccessTokenAsync(CancellationToken ct)
    {
        var userToken = services.GetService<ITwitchAccessTokenProvider>();
        if (userToken is null)
            throw new InvalidOperationException("Twitch bot runtime is not configured.");

        return await userToken.GetAccessTokenAsync(ct);
    }
}
