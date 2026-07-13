namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchOAuthClient(
    TwitchBotIdentity identity,
    TwitchOAuthApiClient twitch
) : ITwitchOAuthClient
{
    public Uri BuildAuthorizeUri(string state)
    {
        return twitch.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                identity.ClientId,
                identity.RedirectUri,
                identity.Scopes,
                state
            )
        );
    }

    public async Task<TwitchTokenSet> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken
    )
    {
        var response = await twitch.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                identity.RedirectUri,
                code
            ),
            cancellationToken
        );
        return ToTokenSet(response);
    }

    public async Task<TwitchTokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        return ToTokenSet(
            await twitch.RefreshAsync(
                identity.ClientId,
                identity.ClientSecret,
                refreshToken,
                cancellationToken
            )
        );
    }

    public async Task<bool> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        return await twitch.ValidateTokenAsync(accessToken, cancellationToken) is not null;
    }

    private static TwitchTokenSet ToTokenSet(TwitchOAuthTokenResponse payload)
    {
        var access = payload.AccessToken;
        var refresh = payload.RefreshToken;
        var expiresIn = payload.ExpiresIn;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30));

        return new TwitchTokenSet(access, refresh, expiresAt);
    }
}
