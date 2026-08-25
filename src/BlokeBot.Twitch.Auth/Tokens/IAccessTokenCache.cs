using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Owns synchronized in-memory Twitch access-token state and invalidation.
/// </summary>
public interface IAccessTokenCache
{
    internal CredentialEpoch Epoch { get; }

    internal Task<TResult> ExecuteSynchronizedAsync<TResult>(
        Func<IAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Deletes a stored token set and clears its loaded in-memory state as one synchronized
    /// operation.
    /// </summary>
    /// <param name="tokenStore">The durable token store to clear.</param>
    /// <param name="path">The storage path to clear.</param>
    /// <param name="cancellationToken">
    /// A token that cancels waiting for synchronized clearing to begin.
    /// </param>
    Task ClearAsync(ITokenStore tokenStore, string path, CancellationToken cancellationToken);
}

internal interface IAccessTokenCacheTransaction
{
    CredentialEpoch Epoch { get; }

    bool IsLoaded { get; }

    Option<TokenSet> Current { get; }

    void SetLoaded(Option<TokenSet> current);
}

public readonly record struct CredentialEpoch(long Value);
