using BlokeBot.Auth.OAuth;
using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class ChannelBotOAuthService(IConfiguration configuration, TwitchOAuthApiClient oauth)
{
    private const string CallbackPath = "/oauth/channel-bot/callback";

    public Uri CreateAuthorizationUri(HttpRequest request, string state)
    {
        var clientId = ClientId();
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("TwitchBot client ID is missing.");

        var scopes = RequestedScopes();
        return oauth.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                clientId,
                OAuthRequestUri.CreateCallbackUri(request, CallbackPath),
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
                OAuthRequestUri.CreateCallbackUri(request, CallbackPath),
                code
            ),
            ct
        );
        var validation = await oauth.ValidateTokenAsync(token.AccessToken, ct);
        if (validation is null)
            throw new InvalidOperationException("Twitch did not finish connecting this channel.");

        return new ChannelBotAuthorizationGrant(
            validation.UserId,
            LoginName.Parse(validation.Login),
            validation.Scopes
        );
    }

    public string[] RequestedScopes() =>
        configuration.GetSection("TwitchBot:ChannelAuthorization:Scopes").Get<string[]>()
            is { } scopes
            ? TwitchScopeSet.NormalizeMany(scopes)
            : [];

    private string? ClientId() => configuration.GetSection("TwitchBot:Identity")["ClientId"];

    private string? ClientSecret() =>
        configuration.GetSection("TwitchBot:Identity")["ClientSecret"];
}
