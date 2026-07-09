using BlokeBot.Identity;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class HostBotAccountOAuthService(
    IOptions<TwitchBotOptions> options,
    TwitchOAuthApiClient oauth,
    TwitchHelixApiClient helix
)
{
    private readonly TwitchBotOptions options = options.Value;

    public Uri CreateAuthorizationUri(string state)
    {
        var identity = options.Identity;
        ValidateConfiguredIdentity(identity, requireSecret: false);

        return oauth.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                identity.ClientId,
                identity.RedirectUri,
                RequestedScopes(),
                state
            )
        );
    }

    public async Task<HostBotAccountAuthorizationGrant> CompleteAsync(
        string code,
        CancellationToken ct
    )
    {
        var identity = options.Identity;
        ValidateConfiguredIdentity(identity, requireSecret: true);

        var token = await oauth.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                identity.RedirectUri,
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

    private static void ValidateConfiguredIdentity(
        TwitchBotIdentityOptions identity,
        bool requireSecret
    )
    {
        if (
            string.IsNullOrWhiteSpace(identity.ClientId)
            || string.IsNullOrWhiteSpace(identity.RedirectUri)
            || (requireSecret && string.IsNullOrWhiteSpace(identity.ClientSecret))
        )
        {
            throw new InvalidOperationException("TwitchBot identity configuration is incomplete.");
        }
    }
}
