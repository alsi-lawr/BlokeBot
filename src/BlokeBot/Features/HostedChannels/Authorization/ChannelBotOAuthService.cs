using System.Net.Http.Json;
using BlokeBot.Identity;
using BlokeBot.Twitch;
using Microsoft.AspNetCore.WebUtilities;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class ChannelBotOAuthService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    TwitchTokenValidationClient tokenValidation
)
{
    public Uri CreateAuthorizationUri(HttpRequest request, string state)
    {
        var clientId = ClientId();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("TwitchBot client ID is missing.");

        var scopes = RequestedScopes();
        var uri = QueryHelpers.AddQueryString(
            "https://id.twitch.tv/oauth2/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = CreateLocalUri(request, "/oauth/channel-bot/callback"),
                ["force_verify"] = "true",
                ["response_type"] = "code",
                ["scope"] = string.Join(' ', scopes),
                ["state"] = state,
            }
        );

        return new Uri(uri);
    }

    public async Task<ChannelBotAuthorizationGrant> CompleteAsync(
        HttpRequest request,
        string code,
        CancellationToken ct
    )
    {
        var clientId = ClientId();
        var clientSecret = ClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("TwitchBot client credentials are missing.");

        using var response = await httpClientFactory
            .CreateClient()
            .PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = clientId,
                        ["client_secret"] = clientSecret,
                        ["code"] = code,
                        ["grant_type"] = "authorization_code",
                        ["redirect_uri"] = CreateLocalUri(request, "/oauth/channel-bot/callback"),
                    }
                ),
                ct
            );

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TwitchAccessTokenResponse>(ct);
        if (string.IsNullOrWhiteSpace(payload?.AccessToken))
            throw new InvalidOperationException("Twitch did not return an access token.");

        var validation = await tokenValidation.ValidateAsync(payload.AccessToken, ct);
        if (validation is null)
            throw new InvalidOperationException("Twitch did not validate the channel authorization grant.");

        return new ChannelBotAuthorizationGrant(
            validation.UserId,
            LoginName.Parse(validation.Login),
            validation.Scopes
        );
    }

    public string[] RequestedScopes() =>
        configuration
            .GetSection("TwitchBot:ChannelAuthorization:Scopes")
            .Get<string[]>()
        is { } scopes
            ? TwitchScopeSet.NormalizeMany(scopes)
            : [];

    private string? ClientId() => configuration.GetSection("TwitchBot:Identity")["ClientId"];

    private string? ClientSecret() =>
        configuration.GetSection("TwitchBot:Identity")["ClientSecret"];

    private static string CreateLocalUri(HttpRequest request, string path)
    {
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        return $"{request.Scheme}://{request.Host}{pathBase}{path}";
    }
}
