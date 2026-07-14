namespace BlokeBot.Twitch.Auth;

public sealed record AuthorizationUriRequest(
    string ClientId,
    string RedirectUri,
    OAuthAuthorizationScopeSet Scopes,
    string State,
    AuthorizationVerificationPolicy Verification
);
