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
    public IO<string, AccessTokenUnavailableReason> GetAccessToken() =>
        IO<string, AccessTokenUnavailableReason>.Create(async cancellationToken =>
            await cache.ExecuteSynchronizedAsync(GetAccessTokenSynchronizedAsync, cancellationToken)
        );

    private async Task<
        Result<string, AccessTokenUnavailableReason>
    > GetAccessTokenSynchronizedAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (!transaction.IsLoaded)
        {
            return await LoadAndGetAccessTokenAsync(transaction, cancellationToken);
        }

        var accessToken = await TryGetAccessTokenAsync(transaction, cancellationToken);
        return await accessToken.Match(
            token => Task.FromResult(Result<string, AccessTokenUnavailableReason>.Success(token)),
            () => LoadAndGetAccessTokenAsync(transaction, cancellationToken)
        );
    }

    private async Task<Result<string, AccessTokenUnavailableReason>> LoadAndGetAccessTokenAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        var loadedToken = await tokenStore.LoadAsync(identity.TokenCachePath, cancellationToken);
        return await loadedToken.Match(
            current => GetLoadedAccessTokenAsync(current, transaction, cancellationToken),
            () =>
            {
                transaction.SetLoaded(Option<TokenSet>.None);
                return Task.FromResult(
                    Result<string, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.MissingRefreshToken
                    )
                );
            }
        );
    }

    private Task<Option<string>> TryGetAccessTokenAsync(
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    ) =>
        transaction.Current.Match(
            current => TryGetCurrentAccessTokenAsync(current, transaction, cancellationToken),
            static () => Task.FromResult(Option<string>.None)
        );

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
            transaction.SetLoaded(Option<TokenSet>.Some(current));
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

    private async Task<Result<string, AccessTokenUnavailableReason>> GetLoadedAccessTokenAsync(
        TokenSet current,
        IAccessTokenCacheTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        var accessToken = await TryGetCurrentAccessTokenAsync(
            current,
            transaction,
            cancellationToken
        );
        return accessToken.Match(
            Result<string, AccessTokenUnavailableReason>.Success,
            () =>
            {
                transaction.SetLoaded(Option<TokenSet>.None);
                return Result<string, AccessTokenUnavailableReason>.Error(
                    AccessTokenUnavailableReason.MissingRefreshToken
                );
            }
        );
    }
}
