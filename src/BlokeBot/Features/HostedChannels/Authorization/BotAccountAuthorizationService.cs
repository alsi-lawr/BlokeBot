using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Authorization;

public enum BotAccountAuthorizationState
{
    Disabled,
    Unknown,
    NotAuthorized,
    WrongAccount,
    MissingScopes,
    Ready,
}

public sealed record BotAccountAuthorizationStatus(
    string? ConfiguredBotLogin,
    string? AuthorizedLogin,
    string? AuthorizedProfileImageUrl,
    BotAccountAuthorizationState State,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<string> MissingScopes,
    string Message
);

public sealed class BotAccountAuthorizationService(
    IOptions<TwitchBotOptions> options,
    IServiceProvider services,
    TwitchHelixApiClient helix,
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
                null,
                BotAccountAuthorizationState.NotAuthorized,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "No bot account is connected yet."
            );
        }

        if (status.State == TwitchTokenStatusState.Invalid)
        {
            return new(
                configuredBotLogin,
                null,
                null,
                BotAccountAuthorizationState.NotAuthorized,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "BlokeBot could not check the connected bot account."
            );
        }

        if (status.Validation is null)
        {
            return new(
                configuredBotLogin,
                null,
                null,
                BotAccountAuthorizationState.Unknown,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "BlokeBot could not check the bot account right now."
            );
        }

        var authorizedLogin = LoginName.Parse(status.Validation.Login).Value;
        var authorizedProfileImageUrl = await LoadAuthorizedProfileImageUrlAsync(status, ct);
        if (!string.Equals(configuredBotLogin, authorizedLogin, StringComparison.Ordinal))
        {
            return new(
                configuredBotLogin,
                authorizedLogin,
                authorizedProfileImageUrl,
                BotAccountAuthorizationState.WrongAccount,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "The connected Twitch account is not the expected bot account."
            );
        }

        if (status.MissingScopes.Count > 0)
        {
            return new(
                configuredBotLogin,
                authorizedLogin,
                authorizedProfileImageUrl,
                BotAccountAuthorizationState.MissingScopes,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "The bot account needs more Twitch access."
            );
        }

        return new(
            configuredBotLogin,
            authorizedLogin,
            authorizedProfileImageUrl,
            BotAccountAuthorizationState.Ready,
            status.RequiredScopes,
            status.GrantedScopes,
            [],
            "The bot account is ready."
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

    private async Task<string?> LoadAuthorizedProfileImageUrlAsync(
        TwitchTokenStatus status,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(status.AccessToken))
            return null;

        var user = await helix.GetCurrentUserAsync(
            new TwitchHelixRequestContext(options.Value.Identity.ClientId, status.AccessToken),
            ct
        );

        return string.IsNullOrWhiteSpace(user?.ProfileImageUrl) ? null : user.ProfileImageUrl;
    }
}
