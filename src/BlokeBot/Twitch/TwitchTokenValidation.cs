namespace BlokeBot.Twitch;

public sealed record TwitchTokenValidation(
    string UserId,
    string Login,
    IReadOnlySet<string> Scopes
);
