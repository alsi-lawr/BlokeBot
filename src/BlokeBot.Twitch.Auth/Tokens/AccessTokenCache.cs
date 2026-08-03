using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

internal sealed class AccessTokenCache
    : IAccessTokenCache,
        IAccessTokenCacheTransaction,
        IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Option<TokenSet> _current = Option<TokenSet>.None;
    private bool _loaded;

    bool IAccessTokenCacheTransaction.IsLoaded => _loaded;

    Option<TokenSet> IAccessTokenCacheTransaction.Current => _current;

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
            _ = _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _current = Option<TokenSet>.None;
            _loaded = false;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    void IAccessTokenCacheTransaction.SetLoaded(Option<TokenSet> tokenSet)
    {
        _current = tokenSet;
        _loaded = true;
    }

    public void Dispose() => _gate.Dispose();
}
