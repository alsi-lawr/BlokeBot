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
        var state = IssueState();
        return oauth.BuildAuthorizeUri(state);
    }

    public Uri CreateAuthorizationUri(IEnumerable<string?> additionalScopes)
    {
        ArgumentNullException.ThrowIfNull(additionalScopes);
        var state = IssueState();
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
                consumed =>
                    cache.Epoch == consumed.CredentialEpoch
                        ? CompleteConsumedAuthorizationAsync(
                            code,
                            consumed.CredentialEpoch,
                            cancellationToken
                        )
                        : InvalidState(),
                static _ => InvalidState()
            );
    }

    private async Task<OAuthFlowCompletionOutcome> CompleteConsumedAuthorizationAsync(
        string code,
        CredentialEpoch credentialEpoch,
        CancellationToken cancellationToken
    )
    {
        var tokenSet = await oauth.ExchangeCodeAsync(code, cancellationToken);
        return await cache.ExecuteSynchronizedAsync(
            async (transaction, token) =>
            {
                if (transaction.Epoch != credentialEpoch)
                {
                    return new OAuthFlowCompletionOutcome.InvalidState();
                }

                await tokens.SaveAsync(identity.TokenCachePath, tokenSet, token);
                transaction.SetLoaded(Option<TokenSet>.Some(tokenSet));
                return (OAuthFlowCompletionOutcome)
                    new OAuthFlowCompletionOutcome.Completed(tokenSet);
            },
            cancellationToken
        );
    }

    private string IssueState() => states.Issue(cache.Epoch);

    private static Task<OAuthFlowCompletionOutcome> InvalidState() =>
        Task.FromResult<OAuthFlowCompletionOutcome>(new OAuthFlowCompletionOutcome.InvalidState());
}
