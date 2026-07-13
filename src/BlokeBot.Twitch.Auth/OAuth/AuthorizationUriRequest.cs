namespace BlokeBot.Twitch.Auth;

public sealed record AuthorizationUriRequest(
    string ClientId,
    string RedirectUri,
    IEnumerable<string> Scopes,
    string State,
    bool ForceVerify = true
);
