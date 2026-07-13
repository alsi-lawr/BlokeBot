namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchAccessTokenCache
    : ITwitchAccessTokenCache,
        ITwitchAccessTokenCacheTransaction
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TwitchTokenSet? _current;
    private bool _loaded;

    bool ITwitchAccessTokenCacheTransaction.IsLoaded => _loaded;

    TwitchTokenSet? ITwitchAccessTokenCacheTransaction.Current => _current;

    async Task<TResult> ITwitchAccessTokenCache.ExecuteSynchronizedAsync<TResult>(
        Func<ITwitchAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
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

    void ITwitchAccessTokenCacheTransaction.SetLoaded(TwitchTokenSet? tokenSet)
    {
        _current = tokenSet;
        _loaded = true;
    }
}
