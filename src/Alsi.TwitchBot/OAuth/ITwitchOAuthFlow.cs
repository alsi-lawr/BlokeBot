namespace Alsi.TwitchBot;

/// <summary>
/// Coordinates OAuth state, authorization callback handling, and token persistence.
/// </summary>
public interface ITwitchOAuthFlow
{
    /// <summary>
    /// Creates a Twitch authorization URI with a new state value.
    /// </summary>
    /// <returns>The authorization URI.</returns>
    Uri CreateAuthorizationUri();

    /// <summary>
    /// Completes an authorization callback and persists the resulting token set.
    /// </summary>
    /// <param name="code">The authorization code.</param>
    /// <param name="state">The returned state value.</param>
    /// <param name="cancellationToken">A token that cancels callback handling.</param>
    /// <returns>The persisted token set.</returns>
    Task<TwitchTokenSet> CompleteAuthorizationAsync(
        string code,
        string state,
        CancellationToken cancellationToken
    );
}
