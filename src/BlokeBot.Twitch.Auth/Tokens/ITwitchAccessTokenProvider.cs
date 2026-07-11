namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Provides a valid Twitch access token for runtime use.
/// </summary>
public interface ITwitchAccessTokenProvider
{
    /// <summary>
    /// Gets a Twitch access token.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels token acquisition.</param>
    /// <returns>A valid access token.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
