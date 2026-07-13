using BlokeBot.Identity;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class HostBotAccountOAuthService(
    TwitchBotSettings settings,
    OAuthTransport transport,
    HelixClient helix
)
{
    public Uri CreateAuthorizationUri(string state, IEnumerable<string?>? scopes = null)
    {
        var identity = settings.Identity;
        ValidateConfiguredIdentity(identity, requireSecret: false);

        return transport.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                identity.ClientId,
                identity.RedirectUri,
                scopes is null ? RequestedScopes() : ScopeSet.NormalizeMany(scopes),
                state
            )
        );
    }

    public async Task<HostBotAccountAuthorizationGrant> CompleteAsync(
        string code,
        CancellationToken ct
    )
    {
        var identity = settings.Identity;
        ValidateConfiguredIdentity(identity, requireSecret: true);

        var token = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                identity.RedirectUri,
                code
            ),
            ct
        );
        var validation =
            await transport.ValidateTokenAsync(token.AccessToken, ct)
            ?? throw new InvalidOperationException(
                "Twitch did not validate the bot account grant."
            );
        var user = await helix.GetCurrentUserAsync(
            new HelixRequestContext(identity.ClientId, token.AccessToken),
            ct
        );

        return new HostBotAccountAuthorizationGrant(
            new TokenSet(
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

    public string[] RequestedScopes()
    {
        return ScopeSet.NormalizeMany(settings.Identity.Scopes);
    }

    private static void ValidateConfiguredIdentity(BotIdentity identity, bool requireSecret)
    {
        if (
            string.IsNullOrWhiteSpace(identity.ClientId)
            || string.IsNullOrWhiteSpace(identity.RedirectUri)
            || (requireSecret && string.IsNullOrWhiteSpace(identity.ClientSecret))
        )
        {
            throw new InvalidOperationException("The bot account is not set up yet.");
        }
    }
}
