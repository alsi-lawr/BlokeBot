namespace Alsi.TwitchBot;

public sealed record TwitchAuthorizationUriRequest(
    string ClientId,
    string RedirectUri,
    IEnumerable<string> Scopes,
    string State,
    bool ForceVerify = true
);
