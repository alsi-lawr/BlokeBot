using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchAccessTokenProvider(
    IOptions<TwitchBotIdentityOptions> options,
    ITwitchTokenStore tokenStore,
    ITwitchOAuthClient oauth
) : ITwitchAccessTokenProvider, ITwitchAccessTokenCache
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TwitchTokenSet? state;
    private bool loaded;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await LoadTokenIfNeededAsync(cancellationToken);

            var accessToken = await TryGetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(accessToken))
                return accessToken;

            await LoadTokenAsync(cancellationToken);
            accessToken = await TryGetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(accessToken))
                return accessToken;

            throw new TwitchAccessTokenUnavailableException(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
            );
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
            state = null;
            loaded = true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task LoadTokenAsync(CancellationToken cancellationToken)
    {
        state = await tokenStore.LoadAsync(options.Value.TokenCachePath, cancellationToken);
        loaded = true;
    }

    private async Task LoadTokenIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!loaded)
            await LoadTokenAsync(cancellationToken);
    }

    private async Task<string?> TryGetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (
            state is { } current
            && current.ExpiresAtUtc > DateTimeOffset.UtcNow
            && await oauth.ValidateAsync(current.AccessToken, cancellationToken)
        )
            return current.AccessToken;

        if (state is { RefreshToken.Length: > 0 } refreshable)
        {
            var refreshed = await oauth.RefreshAsync(refreshable.RefreshToken, cancellationToken);
            state = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                ? refreshed with
                {
                    RefreshToken = refreshable.RefreshToken,
                }
                : refreshed;
            await tokenStore.SaveAsync(options.Value.TokenCachePath, state, cancellationToken);
            return state.AccessToken;
        }

        return null;
    }
}
