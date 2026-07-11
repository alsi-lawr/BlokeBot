namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Owns synchronized in-memory Twitch access-token state and invalidation.
/// </summary>
public interface ITwitchAccessTokenCache
{
    internal Task<TResult> ExecuteSynchronizedAsync<TResult>(
        Func<ITwitchAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Clears loaded in-memory token state so the next access reloads the token store.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels cache clearing.</param>
    Task ClearAsync(CancellationToken cancellationToken);
}

internal interface ITwitchAccessTokenCacheTransaction
{
    bool IsLoaded { get; }

    TwitchTokenSet? Current { get; }

    void SetLoaded(TwitchTokenSet? current);
}
