using System.Threading.Channels;
using BlokeBot.Functional;
using Shouldly;

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
        store.Consume(state).Match(static _ => true, static _ => false).ShouldBeTrue();
        store.Consume(state).Match(static _ => true, static _ => false).ShouldBeFalse();
        store.Consume("missing").Match(static _ => true, static _ => false).ShouldBeFalse();
    }

    [Test]
    public async Task InvalidOAuthState_CompletingFlow_RejectsAuthorization()
    {
        var oauth = new FakeOAuthClient();
        var store = new MemoryTokenStore();
        using var cache = new AccessTokenCache();
        var flow = new OAuthFlow(
            IdentityWithPath("tokens.json"),
            oauth,
            new InMemoryOAuthStateStore(),
            store,
            cache
        );

        var outcome = await flow.CompleteAuthorizationAsync(
            "code",
            "bad-state",
            CancellationToken.None
        );

        _ = outcome.ShouldBeOfType<OAuthFlowCompletionOutcome.InvalidState>();
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
        using var cache = new AccessTokenCache();
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);
        var state = IssuedState(flow);

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
        using var cache = new AccessTokenCache();
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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var first = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await loadStarted.Reader.ReadAsync(CancellationToken.None);
        var second = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        await continueLoad.Writer.WriteAsync(true, CancellationToken.None);
        var accessTokens = (await Task.WhenAll(first, second)).Select(Success).ToArray();

        accessTokens.ShouldAllBe(static accessToken => accessToken == "cached");
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
        using var cache = new AccessTokenCache();
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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var unavailable = Error(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );
        unavailable.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        store.LoadCalls.ShouldBe(1);

        store.Loaded = new TokenSet("authorized", "refresh", _currentTime.AddHours(1));

        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("authorized");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task ValidationFault_RequestingAgain_ReloadsWithoutPublishingFailedToken()
    {
        var failure = new IOException("validation failed");
        var oauth = new FakeOAuthClient { ValidateException = failure };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("failed", "refresh", _currentTime.AddHours(1)),
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var thrown = await Should.ThrowAsync<IOException>(() =>
            provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask()
        );

        thrown.ShouldBeSameAs(failure);
        store.LoadCalls.ShouldBe(1);
        store.SaveCalls.ShouldBe(0);
        oauth.ValidateException = null;
        oauth.ValidateResult = true;
        store.Loaded = new TokenSet("replacement", "refresh", _currentTime.AddHours(1));

        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("replacement");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task RefreshFault_RequestingAgain_ReloadsWithoutPublishingFailedRefresh()
    {
        var failure = new HttpRequestException("refresh failed");
        var oauth = new FakeOAuthClient { RefreshException = failure };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("expired", "refresh", _currentTime.AddMinutes(-1)),
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var thrown = await Should.ThrowAsync<HttpRequestException>(() =>
            provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask()
        );

        thrown.ShouldBeSameAs(failure);
        store.LoadCalls.ShouldBe(1);
        store.SaveCalls.ShouldBe(0);
        oauth.RefreshCalls.ShouldBe(1);
        oauth.RefreshException = null;
        oauth.ValidateResult = true;
        store.Loaded = new TokenSet("replacement", "refresh", _currentTime.AddHours(1));

        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("replacement");
        store.LoadCalls.ShouldBe(2);
        store.SaveCalls.ShouldBe(0);
        oauth.RefreshCalls.ShouldBe(1);
    }

    [Test]
    public async Task InvalidationQueuedBehindLoad_RequestingAgain_ForcesReload()
    {
        var loadStarted = Channel.CreateUnbounded<bool>();
        var continueLoad = Channel.CreateUnbounded<bool>();
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("first", "refresh", _currentTime.AddHours(1)),
            LoadStarted = loadStarted,
            ContinueLoad = continueLoad,
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var loading = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await loadStarted.Reader.ReadAsync(CancellationToken.None);
        var invalidation = cache.ClearAsync(CancellationToken.None);
        await continueLoad.Writer.WriteAsync(true, CancellationToken.None);
        Success(await loading).ShouldBe("first");
        await invalidation;
        store.Loaded = new TokenSet("second", "refresh", _currentTime.AddHours(1));

        var reloading = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await loadStarted.Reader.ReadAsync(CancellationToken.None);
        await continueLoad.Writer.WriteAsync(true, CancellationToken.None);

        Success(await reloading).ShouldBe("second");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task CancellationQueuedBehindLoad_PropagatesAndLaterRequestSucceeds()
    {
        var loadStarted = Channel.CreateUnbounded<bool>();
        var continueLoad = Channel.CreateUnbounded<bool>();
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("cached", "refresh", _currentTime.AddHours(1)),
            LoadStarted = loadStarted,
            ContinueLoad = continueLoad,
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, new FakeOAuthClient { ValidateResult = true });
        var loading = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await loadStarted.Reader.ReadAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var queued = provider.GetAccessToken().ExecuteAsync(cancellation.Token).AsTask();

        await cancellation.CancelAsync();
        var thrown = await Should.ThrowAsync<OperationCanceledException>(() => queued);

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        await continueLoad.Writer.WriteAsync(true, CancellationToken.None);
        Success(await loading).ShouldBe("cached");
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("cached");
        store.LoadCalls.ShouldBe(1);
    }

    [Test]
    public async Task CancellationDuringStoreLoad_PropagatesAndLaterRetrySucceeds()
    {
        var loadStarted = Channel.CreateUnbounded<bool>();
        var continueLoad = Channel.CreateUnbounded<bool>();
        var store = new MemoryTokenStore { LoadStarted = loadStarted, ContinueLoad = continueLoad };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, new FakeOAuthClient { ValidateResult = true });
        using var cancellation = new CancellationTokenSource();
        var loading = provider.GetAccessToken().ExecuteAsync(cancellation.Token).AsTask();
        _ = await loadStarted.Reader.ReadAsync(CancellationToken.None);

        await cancellation.CancelAsync();
        var thrown = await Should.ThrowAsync<OperationCanceledException>(() => loading);

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        store.Loaded = new TokenSet("recovered", "refresh", _currentTime.AddHours(1));
        var retry = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await loadStarted.Reader.ReadAsync(CancellationToken.None);
        await continueLoad.Writer.WriteAsync(true, CancellationToken.None);

        Success(await retry).ShouldBe("recovered");
        store.LoadCalls.ShouldBe(2);
    }

    [Test]
    public async Task ClearedCache_RequestingAccess_ReloadsCurrentStoredToken()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("first", "refresh", _currentTime.AddHours(1)),
        };
        using var cache = new AccessTokenCache();
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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, new FakeOAuthClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            provider.GetAccessToken().ExecuteAsync(cancellation.Token).AsTask()
        );

        store.LoadCalls.ShouldBe(0);
    }

    [Test]
    public async Task TokenStoreFailure_RequestingAccess_PropagatesUnexpectedFailure()
    {
        var loadError = new IOException("Token load failed.");
        var store = new MemoryTokenStore { LoadException = loadError };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, new FakeOAuthClient { ValidateResult = true });

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
            static value => value,
            static () =>
                throw new InvalidOperationException("Expected the stored token to be loaded.")
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

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
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
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("old-access");
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);
        var state = IssuedState(flow);

        _ = await flow.CompleteAuthorizationAsync("code", state, CancellationToken.None);
        var accessToken = Success(
            await provider.GetAccessToken().ExecuteAsync(CancellationToken.None)
        );

        accessToken.ShouldBe("new-access");
        store.Saved.ShouldBe(oauth.ExchangeResult);
    }

    private static string IssuedState(OAuthFlow flow)
    {
        var query = flow.CreateAuthorizationUri().Query.TrimStart('?');
        var state = query
            .Split('&')
            .Single(static parameter => parameter.StartsWith("state=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(state["state=".Length..]);
    }

    private static AccessTokenProvider Provider(
        IAccessTokenCache cache,
        ITokenStore store,
        IOAuthClient oauth
    ) =>
        new(
            IdentityWithPath("tokens.json"),
            cache,
            store,
            oauth,
            new FixedTimeProvider(_currentTime)
        );

    private static string Success(Result<string, AccessTokenUnavailableReason> result) =>
        result.Match(
            static value => value,
            static error =>
                throw new InvalidOperationException($"Expected a token, received {error}.")
        );

    private static AccessTokenUnavailableReason Error(
        Result<string, AccessTokenUnavailableReason> result
    ) =>
        result.Match(
            static _ => throw new InvalidOperationException("Expected token unavailability."),
            static error => error
        );

    private static BotIdentity IdentityWithPath(string path) =>
        BotIdentity.FromOptions(
            new BotIdentityOptions
            {
                BotUsername = "bot",
                ClientId = "client",
                ClientSecret = "secret",
                RedirectUri = "http://localhost/callback",
                TokenCachePath = path,
            }
        );

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
