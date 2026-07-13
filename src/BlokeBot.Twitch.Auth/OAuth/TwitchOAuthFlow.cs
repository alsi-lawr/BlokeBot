namespace BlokeBot.Twitch.Auth;

internal sealed class TwitchOAuthFlow(
    TwitchBotIdentity identity,
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
        {
            throw new InvalidOperationException("Invalid OAuth state.");
        }

        var tokenSet = await oauth.ExchangeCodeAsync(code, cancellationToken);
        await tokens.SaveAsync(identity.TokenCachePath, tokenSet, cancellationToken);
        return tokenSet;
    }
}
