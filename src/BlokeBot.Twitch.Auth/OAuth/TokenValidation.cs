namespace BlokeBot.Twitch.Auth;

public sealed record TokenValidation(string UserId, string Login, IReadOnlySet<string> Scopes);
