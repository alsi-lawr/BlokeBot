using Microsoft.Extensions.Options;

namespace Alsi.TwitchBot;

internal sealed class TwitchOAuthFlow(
    IOptions<TwitchBotOptions> options,
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
        await tokens.SaveAsync(options.Value.Identity.TokenCachePath, tokenSet, cancellationToken);
        return tokenSet;
    }
}
