namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchAccessTokenProvider(
    TwitchBotIdentity identity,
    ITwitchAccessTokenCache cache,
    ITwitchTokenStore tokenStore,
    ITwitchOAuthClient oauth
) : ITwitchAccessTokenProvider
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        return cache.ExecuteSynchronizedAsync(GetAccessTokenSynchronizedAsync, cancellationToken);
    }

    private async Task<string> GetAccessTokenSynchronizedAsync(
        ITwitchAccessTokenCacheTransaction transaction,
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

        throw new TwitchAccessTokenUnavailableException(
            TwitchAccessTokenUnavailableReason.MissingRefreshToken,
            TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
        );
    }

    private async Task LoadTokenAsync(
        ITwitchAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        var loadedToken = await tokenStore.LoadAsync(identity.TokenCachePath, cancellationToken);
        transaction.SetLoaded(loadedToken);
    }

    private async Task LoadTokenIfNeededAsync(
        ITwitchAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (!transaction.IsLoaded)
        {
            await LoadTokenAsync(transaction, cancellationToken);
        }
    }

    private async Task<string?> TryGetAccessTokenAsync(
        ITwitchAccessTokenCacheTransaction transaction,
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
