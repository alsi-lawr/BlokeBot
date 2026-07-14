using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Provides a valid Twitch access token for runtime use.
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>
    /// Gets a Twitch access token.
    /// </summary>
    /// <returns>A deferred operation yielding a valid token or an expected unavailable reason.</returns>
    IO<string, AccessTokenUnavailableReason> GetAccessToken();
}
