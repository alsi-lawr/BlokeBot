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

        var epoch = new CredentialEpoch(42);
        var state = store.Issue(epoch);

        state.ShouldNotBeNullOrWhiteSpace();
        store
            .Consume(state)
            .Match(static consumed => consumed.CredentialEpoch, static _ => default)
            .ShouldBe(epoch);
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
    public async Task RefreshInProgress_Disconnecting_RemovesRefreshedCredentials()
    {
        var refreshStarted = Channel.CreateUnbounded<bool>();
        var continueRefresh = Channel.CreateUnbounded<bool>();
        var oauth = new FakeOAuthClient
        {
            RefreshResult = new TokenSet("refreshed", "new-refresh", _currentTime.AddHours(1)),
            RefreshStarted = refreshStarted,
            ContinueRefresh = continueRefresh,
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("expired", "old-refresh", _currentTime.AddMinutes(-1)),
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);

        var refreshing = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await refreshStarted.Reader.ReadAsync(CancellationToken.None);
        var disconnecting = cache.ClearAsync(store, "tokens.json", CancellationToken.None);

        disconnecting.IsCompleted.ShouldBeFalse();
        await continueRefresh.Writer.WriteAsync(true, CancellationToken.None);
        Success(await refreshing).ShouldBe("refreshed");
        await disconnecting;

        store.Loaded.ShouldBeNull();
        Error(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        using var restartedCache = new AccessTokenCache();
        Error(
                await Provider(restartedCache, store, oauth)
                    .GetAccessToken()
                    .ExecuteAsync(CancellationToken.None)
            )
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
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
    public async Task CancellationWhileDisconnectQueued_PreservesDurableAndCachedCredentials()
    {
        var refreshStarted = Channel.CreateUnbounded<bool>();
        var continueRefresh = Channel.CreateUnbounded<bool>();
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            RefreshResult = new TokenSet("refreshed", "new-refresh", _currentTime.AddHours(1)),
            RefreshStarted = refreshStarted,
            ContinueRefresh = continueRefresh,
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("expired", "old-refresh", _currentTime.AddMinutes(-1)),
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        var refreshing = provider.GetAccessToken().ExecuteAsync(CancellationToken.None).AsTask();
        _ = await refreshStarted.Reader.ReadAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var disconnecting = cache.ClearAsync(store, "tokens.json", cancellation.Token);

        await cancellation.CancelAsync();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => disconnecting);
        store.DeleteCalls.ShouldBe(0);
        await continueRefresh.Writer.WriteAsync(true, CancellationToken.None);
        Success(await refreshing).ShouldBe("refreshed");

        _ = store.Loaded.ShouldNotBeNull();
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("refreshed");
    }

    [Test]
    public async Task CancellationAfterDisconnectStarts_CompletesDurableAndMemoryClear()
    {
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var deleteStarted = Channel.CreateUnbounded<bool>();
        var continueDelete = Channel.CreateUnbounded<bool>();
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("connected", "refresh", _currentTime.AddHours(1)),
            DeleteStarted = deleteStarted,
            ContinueDelete = continueDelete,
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");
        using var cancellation = new CancellationTokenSource();
        var disconnecting = cache.ClearAsync(store, "tokens.json", cancellation.Token);
        _ = await deleteStarted.Reader.ReadAsync(CancellationToken.None);

        await cancellation.CancelAsync();
        disconnecting.IsCompleted.ShouldBeFalse();
        await continueDelete.Writer.WriteAsync(true, CancellationToken.None);
        await disconnecting;

        store.Loaded.ShouldBeNull();
        Error(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        using var restartedCache = new AccessTokenCache();
        Error(
                await Provider(restartedCache, store, oauth)
                    .GetAccessToken()
                    .ExecuteAsync(CancellationToken.None)
            )
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }

    [Test]
    public async Task DurableDeleteFailure_Disconnecting_PreservesCachedCredentials()
    {
        var failure = new IOException("delete failed");
        var oauth = new FakeOAuthClient { ValidateResult = true };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("connected", "refresh", _currentTime.AddHours(1)),
            DeleteException = failure,
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");

        var thrown = await Should.ThrowAsync<IOException>(() =>
            cache.ClearAsync(store, "tokens.json", CancellationToken.None)
        );

        thrown.ShouldBeSameAs(failure);
        _ = store.Loaded.ShouldNotBeNull();
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");
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

    [Test]
    public async Task OAuthExchangeInProgress_Disconnecting_RejectsStaleSave()
    {
        var exchangeStarted = Channel.CreateUnbounded<bool>();
        var continueExchange = Channel.CreateUnbounded<bool>();
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            ExchangeResult = new TokenSet(
                "stale-access",
                "stale-refresh",
                _currentTime.AddHours(1)
            ),
            ExchangeStarted = exchangeStarted,
            ContinueExchange = continueExchange,
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("connected", "refresh", _currentTime.AddHours(1)),
        };
        var states = new InMemoryOAuthStateStore();
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);
        var state = IssuedState(flow);

        var completing = flow.CompleteAuthorizationAsync("code", state, CancellationToken.None);
        _ = await exchangeStarted.Reader.ReadAsync(CancellationToken.None);
        await cache.ClearAsync(store, "tokens.json", CancellationToken.None);
        await continueExchange.Writer.WriteAsync(true, CancellationToken.None);

        _ = (await completing).ShouldBeOfType<OAuthFlowCompletionOutcome.InvalidState>();
        store.SaveCalls.ShouldBe(0);
        store.Loaded.ShouldBeNull();
        Error(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }

    [Test]
    public async Task OAuthSaveInProgress_Disconnecting_RemovesSavedCredentials()
    {
        var saveStarted = Channel.CreateUnbounded<bool>();
        var continueSave = Channel.CreateUnbounded<bool>();
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            ExchangeResult = new TokenSet("authorized", "new-refresh", _currentTime.AddHours(1)),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("connected", "old-refresh", _currentTime.AddHours(1)),
            SaveStarted = saveStarted,
            ContinueSave = continueSave,
        };
        var states = new InMemoryOAuthStateStore();
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);
        var completing = flow.CompleteAuthorizationAsync(
            "code",
            IssuedState(flow),
            CancellationToken.None
        );
        _ = await saveStarted.Reader.ReadAsync(CancellationToken.None);

        var disconnecting = cache.ClearAsync(store, "tokens.json", CancellationToken.None);
        disconnecting.IsCompleted.ShouldBeFalse();
        await continueSave.Writer.WriteAsync(true, CancellationToken.None);
        _ = (await completing).ShouldBeOfType<OAuthFlowCompletionOutcome.Completed>();
        await disconnecting;

        store.Loaded.ShouldBeNull();
        Error(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
        using var restartedCache = new AccessTokenCache();
        Error(
                await Provider(restartedCache, store, oauth)
                    .GetAccessToken()
                    .ExecuteAsync(CancellationToken.None)
            )
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }

    [Test]
    public async Task AuthorizationIssuedBeforeDisconnect_CompletingAfterwardRejectsStaleSave()
    {
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            ExchangeResult = new TokenSet(
                "stale-access",
                "stale-refresh",
                _currentTime.AddHours(1)
            ),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("connected", "old-refresh", _currentTime.AddHours(1)),
        };
        var states = new InMemoryOAuthStateStore();
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);
        var state = IssuedState(flow);
        await cache.ClearAsync(store, "tokens.json", CancellationToken.None);

        var completion = await flow.CompleteAuthorizationAsync(
            "code",
            state,
            CancellationToken.None
        );

        _ = completion.ShouldBeOfType<OAuthFlowCompletionOutcome.InvalidState>();
        oauth.ExchangeCalls.ShouldBe(0);
        store.SaveCalls.ShouldBe(0);
        store.Loaded.ShouldBeNull();
        Error(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }

    [Test]
    public async Task CompletedDisconnect_NewAuthorization_ReconnectsAndSurvivesRestart()
    {
        var oauth = new FakeOAuthClient
        {
            ValidateResult = true,
            ExchangeResult = new TokenSet("reconnected", "new-refresh", _currentTime.AddHours(1)),
        };
        var store = new MemoryTokenStore
        {
            Loaded = new TokenSet("connected", "old-refresh", _currentTime.AddHours(1)),
        };
        using var cache = new AccessTokenCache();
        var provider = Provider(cache, store, oauth);
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("connected");
        await cache.ClearAsync(store, "tokens.json", CancellationToken.None);
        var states = new InMemoryOAuthStateStore();
        var flow = new OAuthFlow(IdentityWithPath("tokens.json"), oauth, states, store, cache);

        var completion = await flow.CompleteAuthorizationAsync(
            "code",
            IssuedState(flow),
            CancellationToken.None
        );

        _ = completion.ShouldBeOfType<OAuthFlowCompletionOutcome.Completed>();
        Success(await provider.GetAccessToken().ExecuteAsync(CancellationToken.None))
            .ShouldBe("reconnected");
        using var restartedCache = new AccessTokenCache();
        Success(
                await Provider(restartedCache, store, oauth)
                    .GetAccessToken()
                    .ExecuteAsync(CancellationToken.None)
            )
            .ShouldBe("reconnected");
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
