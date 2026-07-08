using Alsi.TwitchBot;

namespace BlokeBot.Auth.OAuth;

internal sealed class WebOAuthClient(TwitchOAuthApiClient oauth)
{
    private static readonly string[] Scopes = [TwitchScopes.UserReadModeratedChannels];

    public Uri CreateAuthorizationUri(HttpRequest request, WebAuthOptions options, string state)
    {
        return oauth.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                options.ClientId,
                CreateRedirectUri(request, options),
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
                CreateRedirectUri(request, options),
                code
            ),
            ct
        );
        return token.AccessToken;
    }

    private static string CreateRedirectUri(HttpRequest request, WebAuthOptions options)
    {
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return $"{request.Scheme}://{request.Host}{pathBase}{options.CallbackPath}";
    }
}
