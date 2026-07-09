using BlokeBot.Auth.OAuth;
using BlokeBot.Identity;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class HostBotAccountOAuthService(
    IOptions<TwitchBotOptions> options,
    TwitchOAuthApiClient oauth,
    TwitchHelixApiClient helix
)
{
    private const string CallbackPath = "/oauth/host-bot/callback";
    private readonly TwitchBotOptions options = options.Value;

    public Uri CreateAuthorizationUri(HttpRequest request, string state)
    {
        var identity = options.Identity;
        if (string.IsNullOrWhiteSpace(identity.ClientId))
            throw new InvalidOperationException("TwitchBot client ID is missing.");

        return oauth.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                identity.ClientId,
                OAuthRequestUri.CreateCallbackUri(request, CallbackPath),
                RequestedScopes(),
                state
            )
        );
    }

    public async Task<HostBotAccountAuthorizationGrant> CompleteAsync(
        HttpRequest request,
        string code,
        CancellationToken ct
    )
    {
        var identity = options.Identity;
        if (
            string.IsNullOrWhiteSpace(identity.ClientId)
            || string.IsNullOrWhiteSpace(identity.ClientSecret)
        )
        {
            throw new InvalidOperationException("TwitchBot client credentials are missing.");
        }

        var token = await oauth.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                OAuthRequestUri.CreateCallbackUri(request, CallbackPath),
                code
            ),
            ct
        );
        var validation = await oauth.ValidateTokenAsync(token.AccessToken, ct);
        if (validation is null)
            throw new InvalidOperationException("Twitch did not validate the bot account grant.");

        var user = await helix.GetCurrentUserAsync(
            new TwitchHelixRequestContext(identity.ClientId, token.AccessToken),
            ct
        );

        return new HostBotAccountAuthorizationGrant(
            new TwitchTokenSet(
                token.AccessToken,
                token.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)
            ),
            validation.UserId,
            LoginName.Parse(validation.Login),
            string.IsNullOrWhiteSpace(user?.DisplayName) ? validation.Login : user.DisplayName,
            string.IsNullOrWhiteSpace(user?.ProfileImageUrl) ? null : user.ProfileImageUrl,
            validation.Scopes.ToArray()
        );
    }

    public string[] RequestedScopes() => TwitchScopeSet.NormalizeMany(options.Identity.Scopes);
}
