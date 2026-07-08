namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Creates Twitch OAuth requests and validates Twitch access tokens.
/// </summary>
public interface ITwitchOAuthClient
{
    /// <summary>
    /// Builds an authorization URI for a state value.
    /// </summary>
    /// <param name="state">The state value to include in the request.</param>
    /// <returns>The authorization URI.</returns>
    Uri BuildAuthorizeUri(string state);

    /// <summary>
    /// Exchanges an authorization code for tokens.
    /// </summary>
    /// <param name="code">The authorization code.</param>
    /// <param name="cancellationToken">A token that cancels the exchange.</param>
    /// <returns>The token set returned by Twitch.</returns>
    Task<TwitchTokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Refreshes an access token with a refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">A token that cancels the refresh.</param>
    /// <returns>The refreshed token set.</returns>
    Task<TwitchTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Validates an access token with Twitch.
    /// </summary>
    /// <param name="accessToken">The access token to validate.</param>
    /// <param name="cancellationToken">A token that cancels validation.</param>
    /// <returns><see langword="true" /> when Twitch accepts the token.</returns>
    Task<bool> ValidateAsync(string accessToken, CancellationToken cancellationToken);
}
