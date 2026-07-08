using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Auth;

public sealed class TwitchOAuthApiClient(IHttpClientFactory httpClientFactory)
{
    private const string AuthorizationEndpoint = "https://id.twitch.tv/oauth2/authorize";
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string ValidationEndpoint = "https://id.twitch.tv/oauth2/validate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = httpClientFactory.CreateClient("twitch-oauth");

    public Uri CreateAuthorizationUri(TwitchAuthorizationUriRequest request)
    {
        var query = TwitchQueryString.Create(
            new Dictionary<string, string?>
            {
                ["client_id"] = request.ClientId,
                ["force_verify"] = request.ForceVerify ? "true" : null,
                ["redirect_uri"] = request.RedirectUri,
                ["response_type"] = "code",
                ["scope"] = TwitchScopeSet.Format(request.Scopes),
                ["state"] = request.State,
            }
        );

        return new Uri($"{AuthorizationEndpoint}?{query}");
    }

    public async Task<TwitchOAuthTokenResponse> ExchangeCodeAsync(
        TwitchAuthorizationCodeExchange exchange,
        CancellationToken cancellationToken
    )
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = exchange.ClientId,
            ["client_secret"] = exchange.ClientSecret,
            ["code"] = exchange.Code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = exchange.RedirectUri,
        };

        using var response = await http.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(form),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        return ToTokenResponse(
            await response.Content.ReadFromJsonAsync<TwitchTokenPayload>(
                JsonOptions,
                cancellationToken
            )
        );
    }

    public async Task<TwitchOAuthTokenResponse> RefreshAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };

        using var response = await http.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(form),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var refreshed = ToTokenResponse(
            await response.Content.ReadFromJsonAsync<TwitchTokenPayload>(
                JsonOptions,
                cancellationToken
            )
        );
        return string.IsNullOrWhiteSpace(refreshed.RefreshToken)
            ? refreshed with { RefreshToken = refreshToken }
            : refreshed;
    }

    public async Task<TwitchTokenValidation?> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ValidationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<TwitchTokenValidationPayload>(
            JsonOptions,
            cancellationToken
        );
        return payload is null
            ? null
            : new TwitchTokenValidation(
                payload.UserId,
                TwitchLogin.Normalize(payload.Login),
                TwitchScopeSet.NormalizeMany(payload.Scopes).ToHashSet(StringComparer.Ordinal)
            );
    }

    private static TwitchOAuthTokenResponse ToTokenResponse(TwitchTokenPayload? payload)
    {
        if (string.IsNullOrWhiteSpace(payload?.AccessToken))
            throw new InvalidOperationException("Twitch did not return an access token.");

        return new TwitchOAuthTokenResponse(
            payload.AccessToken,
            payload.RefreshToken ?? string.Empty,
            payload.ExpiresIn
        );
    }

    private sealed record TwitchTokenPayload
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed record TwitchTokenValidationPayload
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; init; } = string.Empty;

        [JsonPropertyName("scopes")]
        public IReadOnlyList<string> Scopes { get; init; } = [];
    }
}
