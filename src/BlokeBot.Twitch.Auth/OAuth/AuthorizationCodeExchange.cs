namespace BlokeBot.Twitch.Auth;

public sealed record AuthorizationCodeExchange(
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string Code
);
