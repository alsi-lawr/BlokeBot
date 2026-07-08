using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed class TwitchTokenValidationClient(IHttpClientFactory httpClientFactory)
{
    private readonly HttpClient http = httpClientFactory.CreateClient("twitch-oauth");

    public async Task<TwitchTokenValidation?> ValidateAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://id.twitch.tv/oauth2/validate"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<TwitchTokenValidationResponse>(ct);
        if (payload is null)
            return null;

        return new TwitchTokenValidation(
            payload.UserId,
            payload.Login,
            payload
                .Scopes.Select(NormalizeScope)
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.Ordinal)
        );
    }

    public static string NormalizeScope(string value) => value.Trim().ToLowerInvariant();

    private sealed record TwitchTokenValidationResponse
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; init; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; init; } = string.Empty;

        [JsonPropertyName("scopes")]
        public IReadOnlyList<string> Scopes { get; init; } = [];
    }
}
