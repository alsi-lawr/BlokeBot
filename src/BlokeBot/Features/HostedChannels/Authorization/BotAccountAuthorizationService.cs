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
    TwitchTokenStatusService tokens,
    HostedChannelChangeNotifier changes
)
{
    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(CancellationToken ct)
    {
        var configuredBotLogin = LoginName.Parse(options.Value.Identity.BotUsername).Value;
        var status = await tokens.GetUserAccessTokenStatusAsync(options.Value.Identity.Scopes, ct);

        if (status.State == TwitchTokenStatusState.Unavailable)
        {
            return new(
                configuredBotLogin,
                null,
                BotAccountAuthorizationState.NotAuthorized,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Bot account authorization has not been completed."
            );
        }

        if (status.State == TwitchTokenStatusState.Invalid)
        {
            return new(
                configuredBotLogin,
                null,
                BotAccountAuthorizationState.NotAuthorized,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Bot account authorization could not be verified."
            );
        }

        if (status.Validation is null)
        {
            return new(
                configuredBotLogin,
                null,
                BotAccountAuthorizationState.Unknown,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Bot account authorization status could not be checked."
            );
        }

        var authorizedLogin = LoginName.Parse(status.Validation.Login).Value;
        if (!string.Equals(configuredBotLogin, authorizedLogin, StringComparison.Ordinal))
        {
            return new(
                configuredBotLogin,
                authorizedLogin,
                BotAccountAuthorizationState.WrongAccount,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "The saved authorization belongs to a different Twitch account."
            );
        }

        if (status.MissingScopes.Count > 0)
        {
            return new(
                configuredBotLogin,
                authorizedLogin,
                BotAccountAuthorizationState.MissingScopes,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Bot account authorization is missing configured permissions."
            );
        }

        return new(
            configuredBotLogin,
            authorizedLogin,
            BotAccountAuthorizationState.Ready,
            status.RequiredScopes,
            status.GrantedScopes,
            [],
            "Bot account authorization is current."
        );
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
}
