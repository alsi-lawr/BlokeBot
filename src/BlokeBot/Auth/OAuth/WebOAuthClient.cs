
namespace BlokeBot.Auth.OAuth;

internal sealed class WebOAuthClient(TwitchOAuthApiClient oauth)
{
    private static readonly string[] Scopes = [TwitchScopes.UserReadModeratedChannels];

    public Uri CreateAuthorizationUri(HttpRequest request, WebAuthOptions options, string state)
    {
        return oauth.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                options.ClientId,
                OAuthRequestUri.CreateCallbackUri(request, options.CallbackPath),
                Scopes,
                state
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
        var token = await oauth.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
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
