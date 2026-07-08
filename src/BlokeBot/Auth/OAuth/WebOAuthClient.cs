using System.Text.Json;
using BlokeBot.Twitch;
using Microsoft.AspNetCore.WebUtilities;

namespace BlokeBot.Auth.OAuth;

internal sealed class WebOAuthClient(IHttpClientFactory httpClientFactory)
{
    private const string AuthorizationEndpoint = "https://id.twitch.tv/oauth2/authorize";
    private const string Scope = "user:read:moderated_channels";
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Uri CreateAuthorizationUri(HttpRequest request, WebAuthOptions options, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["force_verify"] = "true",
            ["redirect_uri"] = CreateRedirectUri(request, options),
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state,
        };

        return new Uri(QueryHelpers.AddQueryString(AuthorizationEndpoint, query));
    }

    public async Task<string> ExchangeCodeAsync(
        HttpRequest request,
        WebAuthOptions options,
        string code,
        CancellationToken ct
    )
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = CreateRedirectUri(request, options),
        };

        using var response = await httpClientFactory
            .CreateClient()
            .PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchAccessTokenResponse>(
            JsonOptions,
            ct
        );

        return string.IsNullOrWhiteSpace(payload?.AccessToken)
            ? throw new InvalidOperationException("Twitch did not return an access token.")
            : payload.AccessToken;
    }

    private static string CreateRedirectUri(HttpRequest request, WebAuthOptions options)
    {
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return $"{request.Scheme}://{request.Host}{pathBase}{options.CallbackPath}";
    }
}
