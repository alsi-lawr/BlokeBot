using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Alsi.TwitchBot;

internal sealed class TwitchOAuthClient(
    IOptions<TwitchBotOptions> options,
    IHttpClientFactory factory
) : ITwitchOAuthClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http = factory.CreateClient("twitch-oauth");
    private readonly TwitchBotOptions opts = options.Value;

    public Uri BuildAuthorizeUri(string state)
    {
        var scopes = string.Join(
            ' ',
            opts.Identity.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => scope.Trim())
                .Distinct(StringComparer.Ordinal)
        );
        var qs =
            $"response_type=code"
            + $"&client_id={Uri.EscapeDataString(opts.Identity.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(opts.Identity.RedirectUri)}"
            + $"&force_verify=true"
            + $"&scope={Uri.EscapeDataString(scopes)}"
            + $"&state={Uri.EscapeDataString(state)}";

        return new Uri($"https://id.twitch.tv/oauth2/authorize?{qs}");
    }

    public async Task<TwitchTokenSet> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken
    )
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
            cancellationToken
        );
        resp.EnsureSuccessStatusCode();

        var payload = await ReadJsonAsync(resp, cancellationToken);
        return ToTokenSet(payload);
    }

    public async Task<TwitchTokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
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
            cancellationToken
        );
        resp.EnsureSuccessStatusCode();

        var refreshed = ToTokenSet(await ReadJsonAsync(resp, cancellationToken));
        return string.IsNullOrWhiteSpace(refreshed.RefreshToken)
            ? refreshed with
            {
                RefreshToken = refreshToken,
            }
            : refreshed;
    }

    public async Task<bool> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            "https://id.twitch.tv/oauth2/validate"
        );
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await http.SendAsync(req, cancellationToken);
        return resp.IsSuccessStatusCode;
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage resp,
        CancellationToken cancellationToken
    )
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.Clone();
    }

    private static TwitchTokenSet ToTokenSet(JsonElement payload)
    {
        var access = payload.GetProperty("access_token").GetString() ?? "";
        var refresh = payload.TryGetProperty("refresh_token", out var rt)
            ? rt.GetString() ?? ""
            : "";
        var expiresIn = payload.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30));

        return new TwitchTokenSet(access, refresh, expiresAt);
    }
}
