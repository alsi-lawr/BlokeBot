namespace BlokeBot.Twitch.Auth;

public sealed record OAuthTokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
