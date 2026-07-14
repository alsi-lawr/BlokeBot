namespace BlokeBot.Twitch.Auth;

public sealed record TokenValidation(string UserId, string Login, OAuthScopeSet Scopes);
