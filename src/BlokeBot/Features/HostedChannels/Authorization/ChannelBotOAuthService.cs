using BlokeBot.Auth.OAuth;
using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class ChannelBotOAuthService(IConfiguration configuration, OAuthTransport transport)
{
    private const string _callbackPath = "/oauth/channel-bot/callback";

    public OAuthAuthorizationStartOutcome CreateAuthorization(HttpRequest request, string state)
    {
        var clientId = AuthorizationClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new OAuthAuthorizationStartOutcome.ConfigurationUnavailable();
        }

        return new OAuthAuthorizationStartOutcome.Ready(
            transport.CreateAuthorizationUri(
                new AuthorizationUriRequest(
                    clientId,
                    OAuthRequestUri.CreateCallbackUri(request, _callbackPath),
                    RequestedScopes(),
                    state,
                    AuthorizationVerificationPolicy.ForceAccountVerification
                )
            )
        );
    }

    public async Task<
        OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>
    > CompleteAsync(HttpRequest request, string code, CancellationToken ct)
    {
        var clientId = TokenExchangeClientId();
        var clientSecret = TokenExchangeClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>.ConfigurationUnavailable();
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
        return (await transport.ValidateTokenAsync(token.AccessToken, ct)).Match<
            OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>
        >(
            validated => new OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>.Completed(
                new ChannelBotAuthorizationGrant(
                    validated.Validation.UserId,
                    LoginName.Parse(validated.Validation.Login),
                    validated.Validation.Scopes
                )
            ),
            static _ => new OAuthAuthorizationCompletionOutcome<ChannelBotAuthorizationGrant>.ProviderNotValidated()
        );
    }

    public OAuthAuthorizationScopeSet RequestedScopes()
    {
        var scopes = configuration
            .GetSection("TwitchBot:ChannelAuthorization:Scopes")
            .Get<string[]>()
            is { } configuredScopes
            ? configuredScopes
            : [];
        return OAuthAuthorizationScopeSet.Create(scopes);
    }

    private string AuthorizationClientId()
    {
        return configuration.GetSection("TwitchBot:Identity")["ClientId"] ?? string.Empty;
    }

    private string TokenExchangeClientId()
    {
        return configuration.GetSection("TwitchBot:Identity")["ClientId"] ?? string.Empty;
    }

    private string TokenExchangeClientSecret()
    {
        return configuration.GetSection("TwitchBot:Identity")["ClientSecret"] ?? string.Empty;
    }
}
