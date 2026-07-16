using BlokeBot.Core.Identity;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public sealed class HostBotAccountOAuthService(
    BotSettings settings,
    OAuthTransport transport,
    HelixClient helix
)
{
    public OAuthAuthorizationStartOutcome CreateAuthorizationUriForDefaultScopes(string state)
    {
        return CreateAuthorizationUriForScopes(state, RequestedScopes());
    }

    public OAuthAuthorizationStartOutcome CreateAuthorizationUriForScopes(
        string state,
        OAuthAuthorizationScopeSet scopes
    )
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var identity = settings.Identity;
        if (!IsConfiguredForAuthorization(identity))
        {
            return new OAuthAuthorizationStartOutcome.ConfigurationUnavailable();
        }

        return new OAuthAuthorizationStartOutcome.Ready(
            transport.CreateAuthorizationUri(
                new AuthorizationUriRequest(
                    identity.ClientId,
                    identity.RedirectUri,
                    scopes,
                    state,
                    AuthorizationVerificationPolicy.ForceAccountVerification
                )
            )
        );
    }

    public async Task<
        OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>
    > CompleteAsync(string code, CancellationToken ct)
    {
        var identity = settings.Identity;
        if (!IsConfiguredForTokenExchange(identity))
        {
            return new OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.ConfigurationUnavailable();
        }

        var token = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                identity.ClientId,
                identity.ClientSecret,
                identity.RedirectUri,
                code
            ),
            ct
        );
        return await (await transport.ValidateTokenAsync(token.AccessToken, ct)).Match(
            validated => CompleteValidatedAuthorizationAsync(token, validated.Validation, ct),
            static _ =>
                Task.FromResult<
                    OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>
                >(
                    new OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.ProviderNotValidated()
                )
        );
    }

    public OAuthAuthorizationScopeSet RequestedScopes()
    {
        return settings.Identity.Scopes;
    }

    private async Task<
        OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>
    > CompleteValidatedAuthorizationAsync(
        OAuthTokenResponse token,
        TokenValidation validation,
        CancellationToken ct
    )
    {
        var identity = settings.Identity;
        var user = await helix.GetCurrentUserAsync(
            new HelixRequestContext(identity.ClientId, token.AccessToken),
            ct
        );

        return new OAuthAuthorizationCompletionOutcome<HostBotAccountAuthorizationGrant>.Completed(
            new HostBotAccountAuthorizationGrant(
                new TokenSet(
                    token.AccessToken,
                    token.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)
                ),
                validation.UserId,
                LoginName.Parse(validation.Login),
                string.IsNullOrWhiteSpace(user?.DisplayName) ? validation.Login : user.DisplayName,
                string.IsNullOrWhiteSpace(user?.ProfileImageUrl) ? null : user.ProfileImageUrl,
                validation.Scopes
            )
        );
    }

    private static bool IsConfiguredForAuthorization(BotIdentity identity)
    {
        return !string.IsNullOrWhiteSpace(identity.ClientId)
            && !string.IsNullOrWhiteSpace(identity.RedirectUri);
    }

    private static bool IsConfiguredForTokenExchange(BotIdentity identity)
    {
        return !string.IsNullOrWhiteSpace(identity.ClientId)
            && !string.IsNullOrWhiteSpace(identity.ClientSecret)
            && !string.IsNullOrWhiteSpace(identity.RedirectUri);
    }
}
