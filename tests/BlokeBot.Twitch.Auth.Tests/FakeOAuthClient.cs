namespace BlokeBot.Twitch.Auth.Tests;

internal sealed class FakeOAuthClient : IOAuthClient
{
    public TokenSet ExchangeResult { get; init; } =
        new("exchanged", "refresh", DateTimeOffset.UtcNow.AddHours(1));

    public TokenSet RefreshResult { get; set; } =
        new("refreshed", "refresh", DateTimeOffset.UtcNow.AddHours(1));

    public bool ValidateResult { get; set; }

    public Exception? RefreshException { get; set; }

    public Exception? ValidateException { get; set; }

    public int ExchangeCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public Uri BuildAuthorizeUri(string state) =>
        new($"https://id.twitch.tv/oauth2/authorize?state={state}");

    public Task<TokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        ExchangeCalls++;
        return Task.FromResult(ExchangeResult);
    }

    public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RefreshCalls++;
        if (RefreshException is not null)
        {
            return Task.FromException<TokenSet>(RefreshException);
        }

        return Task.FromResult(RefreshResult);
    }

    public Task<TokenValidationOutcome> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        if (ValidateException is not null)
        {
            return Task.FromException<TokenValidationOutcome>(ValidateException);
        }

        return Task.FromResult<TokenValidationOutcome>(
            ValidateResult
                ? new TokenValidationOutcome.Validated(
                    new TokenValidation("bot-id", "bot", OAuthScopeSet.Create(["chat:read"]))
                )
                : new TokenValidationOutcome.NotValidated()
        );
    }
}
