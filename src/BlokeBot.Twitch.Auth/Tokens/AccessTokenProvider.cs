namespace BlokeBot.Twitch.Auth;

internal sealed class AccessTokenProvider(
    BotIdentity identity,
    IAccessTokenCache cache,
    ITokenStore tokenStore,
    IOAuthClient oauth
) : IAccessTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return cache.ExecuteSynchronizedAsync(GetAccessTokenSynchronizedAsync, cancellationToken);
    }

    private async Task<string> GetAccessTokenSynchronizedAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await LoadTokenIfNeededAsync(transaction, cancellationToken);

        var accessToken = await TryGetAccessTokenAsync(transaction, cancellationToken);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken;
        }

        await LoadTokenAsync(transaction, cancellationToken);
        accessToken = await TryGetAccessTokenAsync(transaction, cancellationToken);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return accessToken;
        }

        throw new AccessTokenUnavailableException(
            AccessTokenUnavailableReason.MissingRefreshToken,
            AccessTokenUnavailableException.MissingRefreshTokenMessage
        );
    }

    private async Task LoadTokenAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        var loadedToken = await tokenStore.LoadAsync(identity.TokenCachePath, cancellationToken);
        transaction.SetLoaded(loadedToken);
    }

    private async Task LoadTokenIfNeededAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (!transaction.IsLoaded)
        {
            await LoadTokenAsync(transaction, cancellationToken);
        }
    }

    private async Task<string?> TryGetAccessTokenAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (
            transaction.Current is { } current
            && current.ExpiresAtUtc > DateTimeOffset.UtcNow
            && await oauth.ValidateAsync(current.AccessToken, cancellationToken)
        )
        {
            return current.AccessToken;
        }

        if (transaction.Current is { RefreshToken.Length: > 0 } refreshable)
        {
            var refreshed = await oauth.RefreshAsync(refreshable.RefreshToken, cancellationToken);
            var refreshedTokenSet = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                ? refreshed with
                {
                    RefreshToken = refreshable.RefreshToken,
                }
                : refreshed;
            await tokenStore.SaveAsync(
                identity.TokenCachePath,
                refreshedTokenSet,
                cancellationToken
            );
            transaction.SetLoaded(refreshedTokenSet);
            return refreshedTokenSet.AccessToken;
        }

        return null;
    }
}
