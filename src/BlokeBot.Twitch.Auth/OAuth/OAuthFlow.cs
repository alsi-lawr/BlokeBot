namespace BlokeBot.Twitch.Auth;

internal sealed class OAuthFlow(
    BotIdentity identity,
    IOAuthClient oauth,
    IOAuthStateStore states,
    ITokenStore tokens
) : IOAuthFlow
{
    public Uri CreateAuthorizationUri()
    {
        var state = states.Issue();
        return oauth.BuildAuthorizeUri(state);
    }

    public async Task<TokenSet> CompleteAuthorizationAsync(
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
