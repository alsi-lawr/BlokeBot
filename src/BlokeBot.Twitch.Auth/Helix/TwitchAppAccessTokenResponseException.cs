namespace BlokeBot.Twitch.Auth;

public sealed class TwitchAppAccessTokenResponseException()
    : InvalidOperationException("Twitch did not return an app access token.");
