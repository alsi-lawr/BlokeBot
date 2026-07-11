namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchAccessTokenCache
    : ITwitchAccessTokenCache,
        ITwitchAccessTokenCacheTransaction
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TwitchTokenSet? current;
    private bool loaded;

    bool ITwitchAccessTokenCacheTransaction.IsLoaded => loaded;

    TwitchTokenSet? ITwitchAccessTokenCacheTransaction.Current => current;

    async Task<TResult> ITwitchAccessTokenCache.ExecuteSynchronizedAsync<TResult>(
        Func<ITwitchAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(this, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            current = null;
            loaded = true;
        }
        finally
        {
            gate.Release();
        }
    }

    void ITwitchAccessTokenCacheTransaction.SetLoaded(TwitchTokenSet? tokenSet)
    {
        current = tokenSet;
        loaded = true;
    }
}
