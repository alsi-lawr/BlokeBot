using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

public interface ITwitchOAuthClient
{
    Uri BuildAuthorizeUri(string state);
    Task<TokenState> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<TokenState> RefreshAsync(string refreshToken, CancellationToken ct);
    Task<bool> ValidateAsync(string accessToken, CancellationToken ct);
}

public sealed class TwitchOAuthClient : ITwitchOAuthClient
{
    private readonly TwitchBotOptions opts;
    private readonly HttpClient http;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public TwitchOAuthClient(IOptions<TwitchBotOptions> options, IHttpClientFactory factory)
    {
        opts = options.Value;
        http = factory.CreateClient("twitch-oauth");
    }

    public Uri BuildAuthorizeUri(string state)
    {
        var scopes = string.Join(' ', opts.Identity.Scopes);
        var qs =
            $"response_type=code"
            + $"&client_id={Uri.EscapeDataString(opts.Identity.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(opts.Identity.RedirectUri)}"
            + $"&scope={Uri.EscapeDataString(scopes)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return new Uri($"https://id.twitch.tv/oauth2/authorize?{qs}");
    }

    public async Task<TokenState> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = opts.Identity.ClientId,
            ["client_secret"] = opts.Identity.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = opts.Identity.RedirectUri,
        };

        using var resp = await http.PostAsync(
            "https://id.twitch.tv/oauth2/token",
            new FormUrlEncodedContent(form),
            ct
        );
        resp.EnsureSuccessStatusCode();

        var payload = await ReadJsonAsync(resp, ct);

        return ToTokenState(payload);
    }

    public async Task<TokenState> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = opts.Identity.ClientId,
            ["client_secret"] = opts.Identity.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };

        using var resp = await http.PostAsync(
            "https://id.twitch.tv/oauth2/token",
            new FormUrlEncodedContent(form),
            ct
        );
        resp.EnsureSuccessStatusCode();

        var payload = await ReadJsonAsync(resp, ct);

        // Twitch may rotate refresh tokens; prefer returned value if present.
        var refreshed = ToTokenState(payload);
        return refreshed with
        {
            RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                ? refreshToken
                : refreshed.RefreshToken,
        };
    }

    public async Task<bool> ValidateAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            "https://id.twitch.tv/oauth2/validate"
        );
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage resp,
        CancellationToken ct
    )
    {
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static TokenState ToTokenState(JsonElement payload)
    {
        var access = payload.GetProperty("access_token").GetString() ?? "";
        var refresh = payload.TryGetProperty("refresh_token", out var rt)
            ? (rt.GetString() ?? "")
            : "";
        var expiresIn = payload.GetProperty("expires_in").GetInt32();

        // small safety margin
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 30);

        return new TokenState(access, refresh, expiresAt);
    }
}
