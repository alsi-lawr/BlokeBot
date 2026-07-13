namespace BlokeBot.Twitch.Auth;

public enum AccessTokenUnavailableReason
{
    MissingRefreshToken,
}

public sealed class AccessTokenUnavailableException(
    AccessTokenUnavailableReason reason,
    string message
) : InvalidOperationException(message)
{
    public const string MissingRefreshTokenMessage =
        "No Twitch refresh token is available. Complete OAuth setup first.";

    public AccessTokenUnavailableReason Reason { get; } = reason;
}
