namespace BlokeBot.Twitch.Auth;

public enum TwitchAccessTokenUnavailableReason
{
    MissingRefreshToken,
}

public sealed class TwitchAccessTokenUnavailableException(
    TwitchAccessTokenUnavailableReason reason,
    string message
) : InvalidOperationException(message)
{
    public const string MissingRefreshTokenMessage =
        "No Twitch refresh token is available. Complete OAuth setup first.";

    public TwitchAccessTokenUnavailableReason Reason { get; } = reason;
}
