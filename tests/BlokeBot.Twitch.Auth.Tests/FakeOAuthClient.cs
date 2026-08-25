using System.Threading.Channels;

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

    public Channel<bool>? ExchangeStarted { get; init; }

    public Channel<bool>? ContinueExchange { get; init; }

    public Channel<bool>? RefreshStarted { get; init; }

    public Channel<bool>? ContinueRefresh { get; init; }

    public int ExchangeCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public Uri BuildAuthorizeUri(string state) =>
        new($"https://id.twitch.tv/oauth2/authorize?state={state}");

    public async Task<TokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        ExchangeCalls++;
        if (ExchangeStarted is not null)
        {
            await ExchangeStarted.Writer.WriteAsync(true, cancellationToken);
        }

        if (ContinueExchange is not null)
        {
            _ = await ContinueExchange.Reader.ReadAsync(cancellationToken);
        }

        return ExchangeResult;
    }

    public async Task<TokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        RefreshCalls++;
        if (RefreshStarted is not null)
        {
            await RefreshStarted.Writer.WriteAsync(true, cancellationToken);
        }

        if (ContinueRefresh is not null)
        {
            _ = await ContinueRefresh.Reader.ReadAsync(cancellationToken);
        }

        return RefreshException is not null ? throw RefreshException : RefreshResult;
    }

    public Task<TokenValidationOutcome> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken
    ) =>
        ValidateException is not null
            ? Task.FromException<TokenValidationOutcome>(ValidateException)
            : Task.FromResult<TokenValidationOutcome>(
                ValidateResult
                    ? new TokenValidationOutcome.Validated(
                        new TokenValidation("bot-id", "bot", OAuthScopeSet.Create(["chat:read"]))
                    )
                    : new TokenValidationOutcome.NotValidated()
            );
}
