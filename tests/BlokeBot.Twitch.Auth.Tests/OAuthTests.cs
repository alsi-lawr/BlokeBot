using System.Threading.Channels;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class OAuthTests
{
    private static readonly DateTimeOffset _currentTime = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

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
            store,
            new AccessTokenCache()
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
            ExchangeResult = new TokenSet("access", "refresh", _currentTime.AddHours(1)),
        };
        var states = new InMemoryOAuthStateStore();
        var store = new MemoryTokenStore();
        var flow = new OAuthFlow(
            IdentityWithPath("tokens.json"),
            oauth,
            states,
            store,
            new AccessTokenCache()
        );
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
            Loaded = new TokenSet("cached", "refresh", _currentTime.AddHours(1)),
        };
        var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("cached");
        oauth.RefreshCalls.ShouldBe(0);
        store.LoadCalls.ShouldBe(1);
    }

    [Test]
    public async Task SimultaneousFirstReads_RequestingValidToken_LoadStoreOnce()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var loadStarted = Channel.CreateUnbounded<bool>();
        var continueLoad = Channel.CreateUnbounded<bool>();
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("cached", "refresh", _currentTime.AddHours(1)),
            LoadStarted = loadStarted,
            ContinueLoad = continueLoad,
        };
        var provider = Provider(new AccessTokenCache(), store, oauth);

        var first = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        await loadStarted.Reader.ReadAsync(CancellationToken.None);
        var second = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        await continueLoad.Writer.WriteAsync(true, CancellationToken.None);
        var accessTokens = (await Task.WhenAll(first, second)).Select(Success).ToArray();

        accessTokens.ShouldAllBe(accessToken => accessToken == "cached");
        store.LoadCalls.ShouldBe(1);
    }

    [Test]
    public async Task ExpiredCachedToken_RequestingAccessToken_RefreshesAndPersistsRotation()
    {
        var oauth = new FakeOAuthClient
        {
            RefreshResult = new TokenSet("new-access", "new-refresh", _currentTime.AddHours(1)),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("old-access", "old-refresh", _currentTime.AddMinutes(-1)),
        };
        var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

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
            RefreshResult = new TokenSet("new-access", "new-refresh", _currentTime.AddHours(1)),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("old-access", "old-refresh", _currentTime.AddMinutes(-1)),
        };
        var provider = Provider(new AccessTokenCache(), store, oauth);

        var requests = Enumerable
            .Range(0, 8)
            .Select(_ => provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask());
        var accessTokens = (await Task.WhenAll(requests)).Select(Success).ToArray();

        accessTokens.ShouldAllBe(accessToken => accessToken == "new-access");
        oauth.RefreshCalls.ShouldBe(1);
        store.SaveCalls.ShouldBe(1);
    }

    [Test]
    public async Task RefreshWithoutRotatedToken_RequestingAccess_PreservesRefreshToken()
    {
        var oauth = new FakeOAuthClient
        {
            RefreshResult = new TokenSet("new-access", string.Empty, _currentTime.AddHours(1)),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("old-access", "old-refresh", _currentTime.AddMinutes(-1)),
        };
        var provider = Provider(new AccessTokenCache(), store, oauth);

        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("new-access");
        store.Saved!.RefreshToken.ShouldBe("old-refresh");
    }

    [Test]
    public async Task RefreshPersistenceFailure_RequestingAgain_DoesNotExposeUnsavedToken()
    {
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            RefreshResult = new TokenSet("new-access", "new-refresh", _currentTime.AddHours(1)),
        };
        var saveError = new IOException("Token save failed.");
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("old-access", "old-refresh", _currentTime.AddMinutes(-1)),
            SaveException = saveError,
        };
        var provider = Provider(new AccessTokenCache(), store, oauth);

        var exception = await Should.ThrowAsync<IOException>(() =>
            provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask()
        );

        exception.Message.ShouldBe(saveError.Message);
        store.Saved.ShouldBeNull();
        oauth.RefreshCalls.ShouldBe(1);

        store.SaveException = null;
        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

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
        var provider = Provider(cache, store, oauth);

        var unavailable = Error(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );
        unavailable.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);

        store.Loaded = new TokenSet("authorized", "refresh", _currentTime.AddHours(1));

        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("authorized");
        store.LoadCalls.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task ClearedCache_RequestingAccess_ReloadsCurrentStoredToken()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("first", "refresh", _currentTime.AddHours(1)),
        };
        var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("first");
        store.Loaded = new TokenSet("second", "refresh", _currentTime.AddHours(1));

        await cache.ClearAsync(CancellationToken.None);
        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("second");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task PreCancelledRequest_RequestingAccess_PropagatesCancellationBeforeLoad()
    {
        var store = new MemoryTokenStore();
        var provider = Provider(new AccessTokenCache(), store, new FakeOAuthClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            provider.GetAccessToken().ExecuteAsync(cancellation.Token).AsTask()
        );

        store.LoadCalls.ShouldBe(0);
    }

    [Test]
    public async Task TokenStoreFailure_RequestingAccess_PropagatesUnexpectedFailure()
    {
        var loadError = new IOException("Token load failed.");
        var store = new MemoryTokenStore { LoadException = loadError };
        var provider = Provider(
            new AccessTokenCache(),
            store,
            new FakeOAuthClient { ValidateResult = true }
        );

        var exception = await Should.ThrowAsync<IOException>(() =>
            provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask()
        );

        exception.Message.ShouldBe(loadError.Message);
        store.LoadCalls.ShouldBe(1);

        store.LoadException = null;
        store.Loaded = new TokenSet("recovered", "refresh", _currentTime.AddHours(1));
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("recovered");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task TokenSet_SavingAndLoadingJsonStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tokens.json");
        var store = new JsonTokenStore();
        var token = new TokenSet("access", "refresh", _currentTime.AddHours(1));

        await store.SaveAsync(path, token, CancellationToken.None);
        var loaded = await store.LoadAsync(path, CancellationToken.None);

        var loadedToken = loaded.Match(
            value => value,
            () => throw new InvalidOperationException("Expected the stored token to be loaded.")
        );
        loadedToken.AccessToken.ShouldBe(token.AccessToken);
        loadedToken.RefreshToken.ShouldBe(token.RefreshToken);
        loadedToken.ExpiresAtUtc.ShouldBe(token.ExpiresAtUtc);
    }

    [Test]
    public async Task ExistingToken_CancelledReplacement_PreservesDurableToken()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "tokens.json");
        var store = new JsonTokenStore();
        var original = new TokenSet("original", "refresh", _currentTime.AddHours(1));
        await store.SaveAsync(path, original, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            store.SaveAsync(
                path,
                new TokenSet("replacement", "refresh", _currentTime.AddHours(2)),
                cancellation.Token
            )
        );
        var loaded = await store.LoadAsync(path, CancellationToken.None);

        loaded
            .Match(
                token => token,
                () => throw new InvalidOperationException("Expected the original stored token.")
            )
            .AccessToken.ShouldBe("original");
    }

    [Test]
    public async Task CompletedAuthorization_AfterCacheWarmup_PublishesPersistedToken()
    {
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            ExchangeResult = new TokenSet("new-access", "new-refresh", _currentTime.AddHours(1)),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("old-access", "old-refresh", _currentTime.AddHours(1)),
        };
        var states = new InMemoryOAuthStateStore();
        var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("old-access");
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);
        var state = flow.CreateAuthorizationUri().Query.Split("state=")[1];

        await flow.CompleteAuthorizationAsync("code", state, CancellationToken.None);
        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("new-access");
        store.Saved.ShouldBe(oauth.ExchangeResult);
    }

    private static AccessTokenProvider Provider(
        IAccessTokenCache cache,
        ITokenStore store,
        IOAuthClient oauth
    )
    {
        return new(
            IdentityWithPath("tokens.json"),
            cache,
            store,
            oauth,
            new FixedTimeProvider(_currentTime)
        );
    }

    private static string Success(Result<string, AccessTokenUnavailableReason> result)
    {
        return result.Match(
            value => value,
            error => throw new InvalidOperationException($"Expected a token, received {error}.")
        );
    }

    private static AccessTokenUnavailableReason Error(
        Result<string, AccessTokenUnavailableReason> result
    )
    {
        return result.Match(
            _ => throw new InvalidOperationException("Expected token unavailability."),
            error => error
        );
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

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return currentTime;
        }
    }
}
