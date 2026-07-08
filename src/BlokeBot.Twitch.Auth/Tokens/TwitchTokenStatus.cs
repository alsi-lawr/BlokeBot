namespace BlokeBot.Twitch.Auth;

public enum TwitchTokenStatusState
{
    Unknown,
    Unavailable,
    Invalid,
    MissingScopes,
    Ready,
}

public sealed record TwitchTokenStatus(
    TwitchTokenStatusState State,
    string? AccessToken,
    TwitchTokenValidation? Validation,
    IReadOnlyList<string> RequiredScopes,
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<string> MissingScopes
);
