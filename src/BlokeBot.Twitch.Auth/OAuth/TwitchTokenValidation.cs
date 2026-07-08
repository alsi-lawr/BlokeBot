namespace BlokeBot.Twitch.Auth;

public sealed record TwitchTokenValidation(
    string UserId,
    string Login,
    IReadOnlySet<string> Scopes
);
