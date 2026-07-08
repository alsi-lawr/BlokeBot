namespace BlokeBot.Twitch.Auth;

public sealed record TwitchOAuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn
);
