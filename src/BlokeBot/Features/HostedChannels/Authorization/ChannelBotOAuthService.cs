using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class ChannelBotOAuthService(
    IConfiguration configuration,
    TwitchOAuthApiClient oauth
)
{
    public Uri CreateAuthorizationUri(HttpRequest request, string state)
    {
        var clientId = ClientId();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("TwitchBot client ID is missing.");

        var scopes = RequestedScopes();
        return oauth.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                clientId,
                CreateLocalUri(request, "/oauth/channel-bot/callback"),
                scopes,
                state
            )
        );
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

        var token = await oauth.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                clientId,
                clientSecret,
                CreateLocalUri(request, "/oauth/channel-bot/callback"),
                code
            ),
            ct
        );
        var validation = await oauth.ValidateTokenAsync(token.AccessToken, ct);
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
