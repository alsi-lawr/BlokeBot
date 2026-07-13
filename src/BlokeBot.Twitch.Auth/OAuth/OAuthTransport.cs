using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Auth;

public sealed class OAuthTransport(IHttpClientFactory httpClientFactory)
{
    private const string _authorizationEndpoint = "https://id.twitch.tv/oauth2/authorize";
    private const string _tokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string _validationEndpoint = "https://id.twitch.tv/oauth2/validate";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-oauth");

    public Uri CreateAuthorizationUri(AuthorizationUriRequest request)
    {
        var query = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["client_id"] = request.ClientId,
                ["force_verify"] = request.ForceVerify ? "true" : null,
                ["redirect_uri"] = request.RedirectUri,
                ["response_type"] = "code",
                ["scope"] = ScopeSet.Format(request.Scopes),
                ["state"] = request.State,
            }
        );

        return new Uri($"{_authorizationEndpoint}?{query}");
    }

    public async Task<OAuthTokenResponse> ExchangeCodeAsync(
        AuthorizationCodeExchange exchange,
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

        using var response = await _http.PostAsync(
            _tokenEndpoint,
            new FormUrlEncodedContent(form),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        return ToTokenResponse(
            await response.Content.ReadFromJsonAsync<TokenPayload>(_jsonOptions, cancellationToken)
        );
    }

    public async Task<OAuthTokenResponse> RefreshAsync(
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

        using var response = await _http.PostAsync(
            _tokenEndpoint,
            new FormUrlEncodedContent(form),
            cancellationToken
        );
        response.EnsureSuccessStatusCode();

        var refreshed = ToTokenResponse(
            await response.Content.ReadFromJsonAsync<TokenPayload>(_jsonOptions, cancellationToken)
        );
        return string.IsNullOrWhiteSpace(refreshed.RefreshToken)
            ? refreshed with
            {
                RefreshToken = refreshToken,
            }
            : refreshed;
    }

    public async Task<TokenValidation?> ValidateTokenAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _validationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<TokenValidationPayload>(
            _jsonOptions,
            cancellationToken
        );
        return payload is null
            ? null
            : new TokenValidation(
                payload.UserId,
                Login.Normalize(payload.Login),
                ScopeSet.NormalizeMany(payload.Scopes).ToHashSet(StringComparer.Ordinal)
            );
    }

    private static OAuthTokenResponse ToTokenResponse(TokenPayload? payload)
    {
        if (string.IsNullOrWhiteSpace(payload?.AccessToken))
        {
            throw new InvalidOperationException("Twitch did not return an access token.");
        }

        return new OAuthTokenResponse(
            payload.AccessToken,
            payload.RefreshToken ?? string.Empty,
            payload.ExpiresIn
        );
    }

    private sealed record TokenPayload
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed record TokenValidationPayload
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; init; } = string.Empty;

        [JsonPropertyName("scopes")]
        public IReadOnlyList<string> Scopes { get; init; } = [];
    }
}
