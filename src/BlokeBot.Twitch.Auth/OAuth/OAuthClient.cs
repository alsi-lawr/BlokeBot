namespace BlokeBot.Twitch.Auth;

internal sealed class OAuthClient(BotIdentity identity, OAuthTransport transport) : IOAuthClient
{
    public Uri BuildAuthorizeUri(string state)
    {
        return transport.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                identity.ClientId,
                identity.RedirectUri,
                identity.Scopes,
                state
            )
        );
    }

    public async Task<TokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var response = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                identity.RedirectUri,
                code
            ),
            cancellationToken
        );
        return ToTokenSet(response);
    }

    public async Task<TokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        return ToTokenSet(
            await transport.RefreshAsync(
                identity.ClientId,
                identity.ClientSecret,
                refreshToken,
                cancellationToken
            )
        );
    }

    public async Task<bool> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        return await transport.ValidateTokenAsync(accessToken, cancellationToken) is not null;
    }

    private static TokenSet ToTokenSet(OAuthTokenResponse payload)
    {
        var access = payload.AccessToken;
        var refresh = payload.RefreshToken;
        var expiresIn = payload.ExpiresIn;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30));

        return new TokenSet(access, refresh, expiresAt);
    }
}
