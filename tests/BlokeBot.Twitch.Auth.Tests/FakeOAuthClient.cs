using BlokeBot.Twitch.Auth;

namespace BlokeBot.Twitch.Auth.Tests;

internal sealed class FakeOAuthClient : ITwitchOAuthClient
{
    public TwitchTokenSet ExchangeResult { get; init; } =
        new("exchanged", "refresh", DateTimeOffset.UtcNow.AddHours(1));

    public TwitchTokenSet RefreshResult { get; init; } =
        new("refreshed", "refresh", DateTimeOffset.UtcNow.AddHours(1));

    public bool ValidateResult { get; init; }

    public int RefreshCalls { get; private set; }

    public Uri BuildAuthorizeUri(string state)
    {
        return new($"https://id.twitch.tv/oauth2/authorize?state={state}");
    }

    public Task<TwitchTokenSet> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(ExchangeResult);
    }

    public Task<TwitchTokenSet> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        RefreshCalls++;
        return Task.FromResult(RefreshResult);
    }

    public Task<bool> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        return Task.FromResult(ValidateResult);
    }
}
