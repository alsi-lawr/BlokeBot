using Microsoft.Extensions.Options;

public interface IAccessTokenProvider
{
    Task<string> GetAsync(CancellationToken ct);
}

public sealed class AccessTokenProvider : IAccessTokenProvider
{
    private readonly TwitchBotOptions opts;
    private readonly ITokenCache cache;
    private readonly ITwitchOAuthClient oauth;

    private readonly SemaphoreSlim gate = new(1, 1);
    private TokenState? state;

    public AccessTokenProvider(
        IOptions<TwitchBotOptions> options,
        ITokenCache cache,
        ITwitchOAuthClient oauth
    )
    {
        opts = options.Value;
        this.cache = cache;
        this.oauth = oauth;
        state = cache.Load(opts.Identity.TokenCachePath);
    }

    public async Task<string> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (
                state is { } s
                && s.ExpiresAtUtc > DateTimeOffset.UtcNow
                && await oauth.ValidateAsync(s.AccessToken, ct)
            )
                return s.AccessToken;

            if (state is { RefreshToken: { Length: > 0 } } rt)
            {
                state = await oauth.RefreshAsync(rt.RefreshToken, ct);
                cache.Save(opts.Identity.TokenCachePath, state);
                return state.AccessToken;
            }

            throw new InvalidOperationException(
                "No Twitch refresh token cached yet. Visit /twitch/oauth/start to authorize the bot account."
            );
        }
        finally
        {
            gate.Release();
        }
    }
}
