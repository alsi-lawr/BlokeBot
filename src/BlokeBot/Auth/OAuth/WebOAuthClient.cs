namespace BlokeBot.Auth.OAuth;

internal sealed class WebOAuthClient(OAuthTransport transport)
{
    private static readonly OAuthAuthorizationScopeSet _scopes = OAuthAuthorizationScopeSet.Create([
        Scopes.UserReadModeratedChannels,
    ]);

    public Uri CreateAuthorizationUri(HttpRequest request, WebAuthOptions options, string state)
    {
        return transport.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                options.ClientId,
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
        var token = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                options.ClientId,
                options.ClientSecret,
                OAuthRequestUri.CreateCallbackUri(request, options.CallbackPath),
                code
            ),
            ct
        );
        return token.AccessToken;
    }
}
