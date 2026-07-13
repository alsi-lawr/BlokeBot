namespace BlokeBot.Twitch.Auth;

public sealed class AppAccessTokenResponseException()
    : InvalidOperationException("Twitch did not return an app access token.");
