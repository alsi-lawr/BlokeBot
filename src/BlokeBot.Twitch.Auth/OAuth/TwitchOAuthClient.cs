using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchOAuthClient(
    IOptions<TwitchBotIdentityOptions> options,
    TwitchOAuthApiClient twitch
) : ITwitchOAuthClient
{
    private readonly TwitchBotIdentityOptions opts = options.Value;

    public Uri BuildAuthorizeUri(string state)
    {
        return twitch.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(opts.ClientId, opts.RedirectUri, opts.Scopes, state)
        );
    }

    public async Task<TwitchTokenSet> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken
    )
    {
        var response = await twitch.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                opts.ClientId,
                opts.ClientSecret,
                opts.RedirectUri,
                code
            ),
            cancellationToken
        );
        return ToTokenSet(response);
    }

    public async Task<TwitchTokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        return ToTokenSet(
            await twitch.RefreshAsync(
                opts.ClientId,
                opts.ClientSecret,
                refreshToken,
                cancellationToken
            )
        );
    }

    public async Task<bool> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken
    ) => await twitch.ValidateTokenAsync(accessToken, cancellationToken) is not null;

    private static TwitchTokenSet ToTokenSet(TwitchOAuthTokenResponse payload)
    {
        var access = payload.AccessToken;
        var refresh = payload.RefreshToken;
        var expiresIn = payload.ExpiresIn;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30));

        return new TwitchTokenSet(access, refresh, expiresAt);
    }
}
