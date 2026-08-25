using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

internal sealed class AccessTokenCache
    : IAccessTokenCache,
        IAccessTokenCacheTransaction,
        IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Option<TokenSet> _current = Option<TokenSet>.None;
    private long _epoch;
    private bool _loaded;

    CredentialEpoch IAccessTokenCache.Epoch => new(Interlocked.Read(ref _epoch));

    CredentialEpoch IAccessTokenCacheTransaction.Epoch => new(_epoch);

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

    public async Task ClearAsync(
        ITokenStore tokenStore,
        string path,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(tokenStore);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var nextEpoch = checked(_epoch + 1);
            await tokenStore.DeleteAsync(path, CancellationToken.None);
            _current = Option<TokenSet>.None;
            _loaded = false;
            _ = Interlocked.Exchange(ref _epoch, nextEpoch);
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
