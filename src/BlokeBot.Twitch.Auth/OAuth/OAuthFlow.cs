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

    public Task<OAuthFlowCompletionOutcome> CompleteAuthorizationAsync(
        string code,
        string state,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return states
            .Consume(state)
            .Match(
                _ => CompleteConsumedAuthorizationAsync(code, cancellationToken),
                static _ =>
                    Task.FromResult<OAuthFlowCompletionOutcome>(
                        new OAuthFlowCompletionOutcome.InvalidState()
                    )
            );
    }

    private async Task<OAuthFlowCompletionOutcome> CompleteConsumedAuthorizationAsync(
        string code,
        CancellationToken cancellationToken
    )
    {
        var tokenSet = await oauth.ExchangeCodeAsync(code, cancellationToken);
        await tokens.SaveAsync(identity.TokenCachePath, tokenSet, cancellationToken);
        return new OAuthFlowCompletionOutcome.Completed(tokenSet);
    }
}
