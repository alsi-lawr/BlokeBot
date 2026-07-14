using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

internal sealed class AccessTokenProvider(
    BotIdentity identity,
    IAccessTokenCache cache,
    ITokenStore tokenStore,
    IOAuthClient oauth,
    TimeProvider timeProvider
) : IAccessTokenProvider
{
    public IO<string, AccessTokenUnavailableReason> GetAccessToken()
    {
        return IO<string, AccessTokenUnavailableReason>.Create(async cancellationToken =>
            await cache.ExecuteSynchronizedAsync(GetAccessTokenSynchronizedAsync, cancellationToken)
        );
    }

    private async Task<
        Result<string, AccessTokenUnavailableReason>
    > GetAccessTokenSynchronizedAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await LoadTokenIfNeededAsync(transaction, cancellationToken);

        var accessToken = await TryGetAccessTokenAsync(transaction, cancellationToken);
        return await accessToken.Match(
            token => Task.FromResult(Result<string, AccessTokenUnavailableReason>.Success(token)),
            async () =>
            {
                await LoadTokenAsync(transaction, cancellationToken);
                var reloaded = await TryGetAccessTokenAsync(transaction, cancellationToken);
                return reloaded.Match(
                    Result<string, AccessTokenUnavailableReason>.Success,
                    static () =>
                        Result<string, AccessTokenUnavailableReason>.Error(
                            AccessTokenUnavailableReason.MissingRefreshToken
                        )
                );
            }
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

    private Task<Option<string>> TryGetAccessTokenAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        return transaction.Current.Match(
            current => TryGetCurrentAccessTokenAsync(current, transaction, cancellationToken),
            static () => Task.FromResult(Option<string>.None)
        );
    }

    private async Task<Option<string>> TryGetCurrentAccessTokenAsync(
        TokenSet current,
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (
            current.ExpiresAtUtc > timeProvider.GetUtcNow()
            && (await oauth.ValidateAsync(current.AccessToken, cancellationToken)).Match(
                static _ => true,
                static _ => false
            )
        )
        {
            return Option<string>.Some(current.AccessToken);
        }

        if (string.IsNullOrWhiteSpace(current.RefreshToken))
        {
            return Option<string>.None;
        }

        var refreshed = await oauth.RefreshAsync(current.RefreshToken, cancellationToken);
        var refreshedTokenSet = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
            ? refreshed with
            {
                RefreshToken = current.RefreshToken,
            }
            : refreshed;
        await tokenStore.SaveAsync(identity.TokenCachePath, refreshedTokenSet, cancellationToken);
        transaction.SetLoaded(Option<TokenSet>.Some(refreshedTokenSet));
        return Option<string>.Some(refreshedTokenSet.AccessToken);
    }
}
