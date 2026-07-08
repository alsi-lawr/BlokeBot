using Microsoft.Extensions.Options;

namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchOAuthFlow(
    IOptions<TwitchBotIdentityOptions> options,
    ITwitchOAuthClient oauth,
    ITwitchOAuthStateStore states,
    ITwitchTokenStore tokens
) : ITwitchOAuthFlow
{
    public Uri CreateAuthorizationUri()
    {
        var state = states.Issue();
        return oauth.BuildAuthorizeUri(state);
    }

    public async Task<TwitchTokenSet> CompleteAuthorizationAsync(
        string code,
        string state,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (!states.Consume(state))
            throw new InvalidOperationException("Invalid OAuth state.");

        var tokenSet = await oauth.ExchangeCodeAsync(code, cancellationToken);
        await tokens.SaveAsync(options.Value.TokenCachePath, tokenSet, cancellationToken);
        return tokenSet;
    }
}
