using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

internal sealed class OAuthFlow(
    BotIdentity identity,
    IOAuthClient oauth,
    IOAuthStateStore states,
    ITokenStore tokens,
    IAccessTokenCache cache
) : IOAuthFlow
{
    public Uri CreateAuthorizationUri()
    {
        var state = states.Issue();
        return oauth.BuildAuthorizeUri(state);
    }

    public Uri CreateAuthorizationUri(IEnumerable<string?> additionalScopes)
    {
        ArgumentNullException.ThrowIfNull(additionalScopes);
        var state = states.Issue();
        return oauth.BuildAuthorizeUri(state, additionalScopes);
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
        await cache.ExecuteSynchronizedAsync(
            async (transaction, token) =>
            {
                await tokens.SaveAsync(identity.TokenCachePath, tokenSet, token);
                transaction.SetLoaded(Option<TokenSet>.Some(tokenSet));
                return true;
            },
            cancellationToken
        );
        return new OAuthFlowCompletionOutcome.Completed(tokenSet);
    }
}
