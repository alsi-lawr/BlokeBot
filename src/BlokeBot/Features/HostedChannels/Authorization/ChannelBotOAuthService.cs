using BlokeBot.Auth.OAuth;
using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class ChannelBotOAuthService(IConfiguration configuration, OAuthTransport transport)
{
    private const string _callbackPath = "/oauth/channel-bot/callback";

    public Uri CreateAuthorizationUri(HttpRequest request, string state)
    {
        var clientId = ClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("TwitchBot client ID is missing.");
        }

        var scopes = RequestedScopes();
        return transport.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                clientId,
                OAuthRequestUri.CreateCallbackUri(request, _callbackPath),
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
        {
            throw new InvalidOperationException("TwitchBot client credentials are missing.");
        }

        var token = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                clientId,
                clientSecret,
                OAuthRequestUri.CreateCallbackUri(request, _callbackPath),
                code
            ),
            ct
        );
        var validation =
            await transport.ValidateTokenAsync(token.AccessToken, ct)
            ?? throw new InvalidOperationException(
                "Twitch did not finish connecting this channel."
            );
        return new ChannelBotAuthorizationGrant(
            validation.UserId,
            LoginName.Parse(validation.Login),
            validation.Scopes
        );
    }

    public string[] RequestedScopes()
    {
        return
            configuration.GetSection("TwitchBot:ChannelAuthorization:Scopes").Get<string[]>()
                is { } scopes
            ? ScopeSet.NormalizeMany(scopes)
            : [];
    }

    private string? ClientId()
    {
        return configuration.GetSection("TwitchBot:Identity")["ClientId"];
    }

    private string? ClientSecret()
    {
        return configuration.GetSection("TwitchBot:Identity")["ClientSecret"];
    }
}
