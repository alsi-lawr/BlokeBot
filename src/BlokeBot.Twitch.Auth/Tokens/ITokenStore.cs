using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Persists Twitch token sets.
/// </summary>
public interface ITokenStore
{
    /// <summary>
    /// Loads a token set from a path.
    /// </summary>
    /// <param name="path">The storage path.</param>
    /// <param name="cancellationToken">A token that cancels loading.</param>
    /// <returns>The optional stored token set.</returns>
    Task<Option<TokenSet>> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a token set to a path.
    /// </summary>
    /// <param name="path">The storage path.</param>
    /// <param name="tokenSet">The token set to save.</param>
    /// <param name="cancellationToken">A token that cancels saving.</param>
    /// <returns>A task that completes when the token set is saved.</returns>
    Task SaveAsync(string path, TokenSet tokenSet, CancellationToken cancellationToken);
}
