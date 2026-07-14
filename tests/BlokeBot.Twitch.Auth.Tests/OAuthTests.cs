using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class OAuthTests
{
    [Test]
    public void IssuedOAuthState_ConsumingTwice_SucceedsOnlyOnce()
    {
        IOAuthStateStore store = new InMemoryOAuthStateStore();

        var state = store.Issue();

        state.ShouldNotBeNullOrWhiteSpace();
        store.Consume(state).ShouldBeOfType<OAuthStateConsumptionOutcome.Consumed>();
        store.Consume(state).ShouldBeOfType<OAuthStateConsumptionOutcome.Rejected>();
        store.Consume("missing").ShouldBeOfType<OAuthStateConsumptionOutcome.Rejected>();
    }

    [Test]
    public async Task InvalidOAuthState_CompletingFlow_RejectsAuthorization()
    {
        var oauth = new FakeOAuthClient();
        var store = new MemoryTokenStore();
        var flow = new OAuthFlow(
            IdentityWithPath("tokens.json"),
            oauth,
            new InMemoryOAuthStateStore(),
            store
        );

        var outcome = await flow.CompleteAuthorizationAsync(
            "code",
            "bad-state",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<OAuthFlowCompletionOutcome.InvalidState>();
        oauth.ExchangeCalls.ShouldBe(0);
        store.Saved.ShouldBeNull();
    }

    [Test]
    public async Task ValidOAuthState_CompletingFlow_ExchangesAndPersistsToken()
    {
        var oauth = new FakeOAuthClient
        {
            ExchangeResult = new TokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var states = new InMemoryOAuthStateStore();
        var store = new MemoryTokenStore();
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store);
        var state = flow.CreateAuthorizationUri().Query.Split("state=")[1];

        var outcome = await flow.CompleteAuthorizationAsync("code", state, CancellationToken.None);
        var token = outcome.ShouldBeOfType<OAuthFlowCompletionOutcome.Completed>().Token;

        token.AccessToken.ShouldBe("access");
        store.Saved.ShouldBe(token);
    }

    [Test]
    public async Task ValidCachedToken_RequestingAccessToken_ReusesWithoutRefresh()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("cached", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var cache = new AccessTokenCache();
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            cache,
            store,
            oauth
        );

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("cached");
        oauth.RefreshCalls.ShouldBe(0);
        store.LoadCalls.ShouldBe(1);
    }

    [Test]
    public async Task SimultaneousFirstReads_RequestingValidToken_LoadStoreOnce()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("cached", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            new AccessTokenCache(),
            store,
            oauth
        );

        var requests = Enumerable
            .Range(0, 8)
            .Select(_ => provider.GetAccessTokenAsync(CancellationToken.None));
        var accessTokens = await Task.WhenAll(requests);

        accessTokens.ShouldAllBe(accessToken => accessToken == "cached");
        store.LoadCalls.ShouldBe(1);
    }

    [Test]
    public async Task ExpiredCachedToken_RequestingAccessToken_RefreshesAndPersistsRotation()
    {
        var oauth = new FakeOAuthClient
        {
            RefreshResult = new TokenSet(
                "new-access",
                "new-refresh",
                DateTimeOffset.UtcNow.AddHours(1)
            ),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet(
                "old-access",
                "old-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-1)
            ),
        };
        var cache = new AccessTokenCache();
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            cache,
            store,
            oauth
        );

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("new-access");
        oauth.RefreshCalls.ShouldBe(1);
        store.Saved!.RefreshToken.ShouldBe("new-refresh");
    }

    [Test]
    public async Task SimultaneousExpiredTokenReads_RequestingAccess_RefreshOnce()
    {
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            RefreshResult = new TokenSet(
                "new-access",
                "new-refresh",
                DateTimeOffset.UtcNow.AddHours(1)
            ),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet(
                "old-access",
                "old-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-1)
            ),
        };
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            new AccessTokenCache(),
            store,
            oauth
        );

        var requests = Enumerable
            .Range(0, 8)
            .Select(_ => provider.GetAccessTokenAsync(CancellationToken.None));
        var accessTokens = await Task.WhenAll(requests);

        accessTokens.ShouldAllBe(accessToken => accessToken == "new-access");
        oauth.RefreshCalls.ShouldBe(1);
        store.SaveCalls.ShouldBe(1);
    }

    [Test]
    public async Task RefreshWithoutRotatedToken_RequestingAccess_PreservesRefreshToken()
    {
        var oauth = new FakeOAuthClient
        {
            RefreshResult = new TokenSet(
                "new-access",
                string.Empty,
                DateTimeOffset.UtcNow.AddHours(1)
            ),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet(
                "old-access",
                "old-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-1)
            ),
        };
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            new AccessTokenCache(),
            store,
            oauth
        );

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("new-access");
        store.Saved!.RefreshToken.ShouldBe("old-refresh");
    }

    [Test]
    public async Task RefreshPersistenceFailure_RequestingAgain_DoesNotExposeUnsavedToken()
    {
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            RefreshResult = new TokenSet(
                "new-access",
                "new-refresh",
                DateTimeOffset.UtcNow.AddHours(1)
            ),
        };
        var saveError = new IOException("Token save failed.");
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet(
                "old-access",
                "old-refresh",
                DateTimeOffset.UtcNow.AddMinutes(-1)
            ),
            SaveException = saveError,
        };
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            new AccessTokenCache(),
            store,
            oauth
        );

        var exception = await Should.ThrowAsync<IOException>(() =>
            provider.GetAccessTokenAsync(CancellationToken.None)
        );

        exception.Message.ShouldBe(saveError.Message);
        store.Saved.ShouldBeNull();
        oauth.RefreshCalls.ShouldBe(1);

        store.SaveException = null;
        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("new-access");
        oauth.RefreshCalls.ShouldBe(2);
        store.SaveCalls.ShouldBe(2);
    }

    [Test]
    public async Task InitiallyMissingToken_RequestingAfterStoreUpdate_ReloadsAndReturnsToken()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore();
        var cache = new AccessTokenCache();
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            cache,
            store,
            oauth
        );

        var exception = await Should.ThrowAsync<AccessTokenUnavailableException>(() =>
            provider.GetAccessTokenAsync(CancellationToken.None)
        );
        exception.Reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);

        store.Loaded = new TokenSet("authorized", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("authorized");
        store.LoadCalls.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task ClearedCache_RequestingAccess_ReloadsCurrentStoredToken()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("first", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var cache = new AccessTokenCache();
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            cache,
            store,
            oauth
        );
        (await provider.GetAccessTokenAsync(CancellationToken.None)).ShouldBe("first");
        store.Loaded = new TokenSet("second", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        await cache.ClearAsync(CancellationToken.None);
        var accessToken = await provider.GetAccessTokenAsync(CancellationToken.None);

        accessToken.ShouldBe("second");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task PreCancelledRequest_RequestingAccess_PropagatesCancellationBeforeLoad()
    {
        var store = new MemoryTokenStore();
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            new AccessTokenCache(),
            store,
            new FakeOAuthClient()
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            provider.GetAccessTokenAsync(cancellation.Token)
        );

        store.LoadCalls.ShouldBe(0);
    }

    [Test]
    public async Task TokenStoreFailure_RequestingAccess_PropagatesUnexpectedFailure()
    {
        var loadError = new IOException("Token load failed.");
        var store = new MemoryTokenStore { LoadException = loadError };
        var provider = new AccessTokenProvider(
            IdentityWithPath("tokens.json"),
            new AccessTokenCache(),
            store,
            new FakeOAuthClient()
        );

        var exception = await Should.ThrowAsync<IOException>(() =>
            provider.GetAccessTokenAsync(CancellationToken.None)
        );

        exception.Message.ShouldBe(loadError.Message);
        store.LoadCalls.ShouldBe(1);
    }

    [Test]
    public async Task TokenSet_SavingAndLoadingJsonStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tokens.json");
        var store = new JsonTokenStore();
        var token = new TokenSet("access", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        await store.SaveAsync(path, token, CancellationToken.None);
        var loaded = await store.LoadAsync(path, CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.AccessToken.ShouldBe(token.AccessToken);
        loaded.RefreshToken.ShouldBe(token.RefreshToken);
        loaded.ExpiresAtUtc.ShouldBe(token.ExpiresAtUtc);
    }

    private static BotIdentity IdentityWithPath(string path)
    {
        return BotIdentity.FromOptions(
            new BotIdentityOptions
            {
                BotUsername = "bot",
                ClientId = "client",
                ClientSecret = "secret",
                RedirectUri = "http://localhost/callback",
                TokenCachePath = path,
            }
        );
    }
}
