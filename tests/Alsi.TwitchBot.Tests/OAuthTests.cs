using Alsi.TwitchBot;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Alsi.TwitchBot.Tests;

public sealed class OAuthTests
{
    [Test]
    public void State_store_issues_and_consumes_once()
    {
        ITwitchOAuthStateStore store = new InMemoryTwitchOAuthStateStore();

        var state = store.Issue();

        state.ShouldNotBeNullOrWhiteSpace();
        store.Consume(state).ShouldBeTrue();
        store.Consume(state).ShouldBeFalse();
        store.Consume("missing").ShouldBeFalse();
    }

    [Test]
    public async Task OAuth_flow_rejects_invalid_state()
    {
        var flow = new TwitchOAuthFlow(
            Options.Create(OptionsWithPath("tokens.json")),
            new FakeOAuthClient(),
            new InMemoryTwitchOAuthStateStore(),
            new MemoryTokenStore()
        );

        await Should.ThrowAsync<InvalidOperationException>(() =>
            flow.CompleteAuthorizationAsync("code", "bad-state", CancellationToken.None)
        );
    }

    [Test]
    public async Task OAuth_flow_exchanges_and_persists_valid_state()
    {
        var oauth = new FakeOAuthClient
        {
            ExchangeResult = new TwitchTokenSet(
                "access",
                "refresh",
                DateTimeOffset.UtcNow.AddHours(1)
            ),
        };
        var states = new InMemoryTwitchOAuthStateStore();
        var store = new MemoryTokenStore();
        var flow = new TwitchOAuthFlow(
            Options.Create(OptionsWithPath("tokens.json")),
            oauth,
            states,
            store
        );
        var state = flow.CreateAuthorizationUri().Query.Split("state=")[1];

        var token = await flow.CompleteAuthorizationAsync("code", state, CancellationToken.None);

        token.AccessToken.ShouldBe("access");
        store.Saved.ShouldBe(token);
    }

    [Test]
    public async Task Access_token_provider_reuses_cached_valid_token()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TwitchTokenSet("cached", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var provider = new TwitchAccessTokenProvider(
            Options.Create(OptionsWithPath("tokens.json")),
            store,
            oauth
        );

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("cached");
        oauth.RefreshCalls.ShouldBe(0);
    }

    [Test]
    public async Task Access_token_provider_refreshes_expired_token_and_persists_rotation()
    {
        var oauth = new FakeOAuthClient
        {
            RefreshResult = new TwitchTokenSet(
                "new-access",
                "new-refresh",
                DateTimeOffset.UtcNow.AddHours(1)
            ),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TwitchTokenSet(
                "old-access",
                "old-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-1)
            ),
        };
        var provider = new TwitchAccessTokenProvider(
            Options.Create(OptionsWithPath("tokens.json")),
            store,
            oauth
        );

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("new-access");
        oauth.RefreshCalls.ShouldBe(1);
        store.Saved!.RefreshToken.ShouldBe("new-refresh");
    }

    [Test]
    public async Task Access_token_provider_reloads_store_after_missing_token()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore();
        var provider = new TwitchAccessTokenProvider(
            Options.Create(OptionsWithPath("tokens.json")),
            store,
            oauth
        );

        await Should.ThrowAsync<InvalidOperationException>(() =>
            provider.GetAccessTokenAsync(CancellationToken.None)
        );

        store.Loaded = new TwitchTokenSet(
            "authorized",
            "refresh",
            DateTimeOffset.UtcNow.AddHours(1)
        );

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("authorized");
        store.LoadCalls.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task Json_token_store_round_trips_tokens()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tokens.json");
        var store = new JsonTwitchTokenStore();
        var token = new TwitchTokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        await store.SaveAsync(path, token, CancellationToken.None);
        var loaded = await store.LoadAsync(path, CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.AccessToken.ShouldBe(token.AccessToken);
        loaded.RefreshToken.ShouldBe(token.RefreshToken);
        loaded.ExpiresAtUtc.ShouldBe(token.ExpiresAtUtc);
    }

    private static TwitchBotOptions OptionsWithPath(string path) =>
        new()
        {
            Identity = new TwitchBotIdentityOptions
            {
                BotUsername = "bot",
                ClientId = "client",
                ClientSecret = "secret",
                RedirectUri = "http://localhost/callback",
                TokenCachePath = path,
            },
        };
}
