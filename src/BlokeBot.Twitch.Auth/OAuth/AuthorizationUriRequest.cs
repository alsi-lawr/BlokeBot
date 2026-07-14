namespace BlokeBot.Twitch.Auth;

public sealed record AuthorizationUriRequest(
    string ClientId,
    string RedirectUri,
    OAuthScopeSet Scopes,
    string State,
    AuthorizationVerificationPolicy Verification
);
