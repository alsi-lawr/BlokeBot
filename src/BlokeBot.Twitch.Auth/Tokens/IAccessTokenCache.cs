namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Owns synchronized in-memory Twitch access-token state and invalidation.
/// </summary>
public interface IAccessTokenCache
{
    internal Task<TResult> ExecuteSynchronizedAsync<TResult>(
        Func<IAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Clears loaded in-memory token state so the next access reloads the token store.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels cache clearing.</param>
    Task ClearAsync(CancellationToken cancellationToken);
}

internal interface IAccessTokenCacheTransaction
{
    bool IsLoaded { get; }

    TokenSet? Current { get; }

    void SetLoaded(TokenSet? current);
}
