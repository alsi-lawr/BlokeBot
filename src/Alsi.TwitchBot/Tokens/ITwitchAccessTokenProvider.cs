namespace Alsi.TwitchBot;

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

/// <summary>
/// Clears the cached Twitch access token for the bot identity.
/// </summary>
public interface ITwitchAccessTokenCache
{
    /// <summary>
    /// Clears any loaded token state so the next access requires a fresh authorization.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels cache clearing.</param>
    Task ClearAsync(CancellationToken cancellationToken);
}
