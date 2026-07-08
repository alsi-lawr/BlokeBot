using Microsoft.Extensions.Options;

namespace Alsi.TwitchBot;

internal sealed class TwitchOAuthClient(
    IOptions<TwitchBotOptions> options,
    TwitchOAuthApiClient twitch
) : ITwitchOAuthClient
{
    private readonly TwitchBotOptions opts = options.Value;

    public Uri BuildAuthorizeUri(string state)
    {
        return twitch.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                opts.Identity.ClientId,
                opts.Identity.RedirectUri,
                opts.Identity.Scopes,
                state
            )
        );
    }

    public async Task<TwitchTokenSet> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken
    )
    {
        var response = await twitch.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                opts.Identity.ClientId,
                opts.Identity.ClientSecret,
                opts.Identity.RedirectUri,
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
                opts.Identity.ClientId,
                opts.Identity.ClientSecret,
                refreshToken,
                cancellationToken
            )
        );
    }

    public async Task<bool> ValidateAsync(string accessToken, CancellationToken cancellationToken) =>
        await twitch.ValidateTokenAsync(accessToken, cancellationToken) is not null;

    private static TwitchTokenSet ToTokenSet(TwitchOAuthTokenResponse payload)
    {
        var access = payload.AccessToken;
        var refresh = payload.RefreshToken;
        var expiresIn = payload.ExpiresIn;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30));

        return new TwitchTokenSet(access, refresh, expiresAt);
    }
}
