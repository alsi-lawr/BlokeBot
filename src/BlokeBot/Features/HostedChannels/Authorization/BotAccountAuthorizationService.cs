using Alsi.TwitchBot;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Twitch;
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
    TwitchTokenValidationClient tokenValidation,
    HostedChannelChangeNotifier changes
)
{
    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken ct)
    {
        var configuredBotLogin = NormalizeLogin(options.Value.Identity.BotUsername);
        var requiredScopes = NormalizeScopes(options.Value.Identity.Scopes);

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
            var validation = await tokenValidation.ValidateAsync(token, ct);
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
            var missingScopes = requiredScopes
                .Except(grantedScopes, StringComparer.Ordinal)
                .ToArray();
            var authorizedLogin = NormalizeLogin(validation.Login);
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

    private static string NormalizeLogin(string value) =>
        value.Trim().TrimStart('#').ToLowerInvariant();

    private static string[] NormalizeScopes(IEnumerable<string> scopes) =>
        scopes
            .Select(TwitchTokenValidationClient.NormalizeScope)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
