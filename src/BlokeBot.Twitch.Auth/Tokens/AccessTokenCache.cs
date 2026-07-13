namespace BlokeBot.Twitch.Auth;

internal sealed class AccessTokenCache : IAccessTokenCache, IAccessTokenCacheTransaction
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TokenSet? _current;
    private bool _loaded;

    bool IAccessTokenCacheTransaction.IsLoaded => _loaded;

    TokenSet? IAccessTokenCacheTransaction.Current => _current;

    async Task<TResult> IAccessTokenCache.ExecuteSynchronizedAsync<TResult>(
        Func<IAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(this, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _current = null;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    void IAccessTokenCacheTransaction.SetLoaded(TokenSet? tokenSet)
    {
        _current = tokenSet;
        _loaded = true;
    }
}
