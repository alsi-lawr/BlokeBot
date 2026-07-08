using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

public sealed class TwitchAppAccessTokenProvider(
    IHttpClientFactory factory,
    IOptions<TwitchBotIdentityOptions> options
)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HttpClient http = factory.CreateClient("twitch-oauth");
    private readonly TwitchBotIdentityOptions opts = options.Value;
    private string? accessToken;
    private DateTimeOffset expiresAtUtc;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (
                !string.IsNullOrWhiteSpace(accessToken)
                && expiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1)
            )
            {
                return accessToken;
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret,
                ["grant_type"] = "client_credentials",
            };

            using var response = await http.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(form),
                cancellationToken
            );
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TwitchAppAccessTokenResponse>(
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(payload?.AccessToken))
                throw new InvalidOperationException("Twitch did not return an app access token.");

            accessToken = payload.AccessToken;
            expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn));
            return accessToken;
        }
        finally
        {
            gate.Release();
        }
    }
}
