namespace BlokeBot.Twitch.Auth;

public sealed record TwitchAuthorizationCodeExchange(
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string Code
);
