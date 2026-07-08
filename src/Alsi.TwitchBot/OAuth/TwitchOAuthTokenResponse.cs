namespace Alsi.TwitchBot;

public sealed record TwitchOAuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn
);
