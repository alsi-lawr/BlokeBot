namespace BlokeBot.Auth.OAuth;

internal sealed class WebOAuthClient(WebAuthConfiguration configuration, OAuthTransport transport)
{
    private static readonly OAuthAuthorizationScopeSet _scopes = OAuthAuthorizationScopeSet.Create([
        Scopes.UserReadModeratedChannels,
    ]);

    public Uri CreateAuthorizationUri(HttpRequest request, WebAuthOptions options, string state)
    {
        var identity = configuration.Identity;
        return transport.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                identity.ClientId,
                OAuthRequestUri.CreateCallbackUri(request, options.CallbackPath),
                _scopes,
                state,
                AuthorizationVerificationPolicy.ForceAccountVerification
            )
        );
    }

    public async Task<string> ExchangeCodeAsync(
        HttpRequest request,
        WebAuthOptions options,
        string code,
        CancellationToken ct
    )
    {
        var identity = configuration.Identity;
        var token = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                OAuthRequestUri.CreateCallbackUri(request, options.CallbackPath),
                code
            ),
            ct
        );
        return token.AccessToken;
    }
}
