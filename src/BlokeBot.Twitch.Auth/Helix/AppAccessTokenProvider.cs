using System.Net.Http.Json;

namespace BlokeBot.Twitch.Auth;

public sealed class AppAccessTokenProvider(
    IHttpClientFactory factory,
    BotIdentity identity,
    TwitchEndpointPolicy endpointPolicy
)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = factory.CreateClient("twitch-oauth");
    private string? _accessToken;
    private DateTimeOffset _expiresAtUtc;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (
                !string.IsNullOrWhiteSpace(_accessToken)
                && _expiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1)
            )
            {
                return _accessToken;
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = identity.ClientId,
                ["client_secret"] = identity.ClientSecret,
                ["grant_type"] = "client_credentials",
            };

            using var response = await _http.PostAsync(
                endpointPolicy.OAuthTokenEndpoint,
                new FormUrlEncodedContent(form),
                cancellationToken
            );
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<AppAccessTokenResponse>(
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                throw new AppAccessTokenResponseException();
            }

            _accessToken = payload.AccessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn));
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
