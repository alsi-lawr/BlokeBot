namespace Alsi.TwitchBot;

public sealed record TwitchTokenValidation(
    string UserId,
    string Login,
    IReadOnlySet<string> Scopes
);
