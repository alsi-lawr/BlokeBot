using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Threading.Channels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BotAccountAuthorizationPolicyTests
{
    [Test]
    public async Task UnavailableToken_LoadingConfiguredStatus_ReportsNotAuthorized()
    {
        var status = await LoadConfiguredStatusAsync(
            new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                RequiredScopes()
            )
        );

        status.State.ShouldBe(BotAccountAuthorizationState.NotAuthorized);
        status.MissingScopes.ShouldBe(RequiredScopes());
    }

    [Test]
    public async Task TokenInspectionFailure_LoadingConfiguredStatus_ReportsUnknown()
    {
        var error = new TokenStatusError.ValidationUnavailable(
            TokenStatusTransportFailureReason.RequestFailed,
            typeof(HttpRequestException).FullName!,
            RequiredScopes()
        );

        var status = await LoadConfiguredStatusAsync(error);

        status.State.ShouldBe(BotAccountAuthorizationState.Unknown);
        status.MissingScopes.ShouldBe(RequiredScopes());
    }

    [Test]
    public async Task MissingTokenScopes_LoadingConfiguredStatus_ReportsMissingScopes()
    {
        var status = await LoadConfiguredStatusAsync(
            new TokenStatus.MissingScopes(
                "saved-token",
                Validation([]),
                RequiredScopes(),
                [],
                RequiredScopes()
            )
        );

        status.State.ShouldBe(BotAccountAuthorizationState.MissingScopes);
        status.AuthorizedLogin.ShouldBe("bot");
        status.MissingScopes.ShouldBe(RequiredScopes());
    }

    [Test]
    public async Task MissingFollowReadScope_LoadingConfiguredStatus_RequiresReconnect()
    {
        var requiredScopes = RequiredScopes();
        var grantedScopes = ImmutableArray.Create(Scopes.UserReadModeratedChannels);
        var status = await LoadConfiguredStatusAsync(
            new TokenStatus.MissingScopes(
                "saved-token",
                Validation(grantedScopes),
                requiredScopes,
                grantedScopes,
                [Scopes.UserReadFollows]
            )
        );

        status.State.ShouldBe(BotAccountAuthorizationState.MissingScopes);
        status.MissingScopes.ShouldBe([Scopes.UserReadFollows]);
    }

    [Test]
    public async Task ReadyToken_LoadingConfiguredStatus_ReportsReady()
    {
        var status = await LoadConfiguredStatusAsync(
            new TokenStatus.Ready(
                "saved-token",
                Validation(RequiredScopes()),
                RequiredScopes(),
                RequiredScopes()
            )
        );

        status.State.ShouldBe(BotAccountAuthorizationState.Ready);
        status.AuthorizedLogin.ShouldBe("bot");
        status.MissingScopes.ShouldBeEmpty();
    }

    [Test]
    public async Task ConfiguredPolicy_ClearingAuthorization_ReportsOnlyAfterDurableAndMemoryClear()
    {
        var tokenCachePath = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-cs004-{Guid.NewGuid():N}.json"
        );
        await File.WriteAllTextAsync(tokenCachePath, "token state");
        try
        {
            var cache = new RecordingAccessTokenCache();
            var tokenStore = new BlockingTokenStore(new JsonTokenStore());
            var events = TestEventBus.Create<AppEventKind>();
            var eventCount = 0;
            using var subscription = events.Subscribe(
                AppEventKind.HostedChannelsChanged,
                ObserverIdentity.Named("Test.BotAccountAuthorizationPolicy"),
                (_, _) =>
                {
                    eventCount++;
                    return ValueTask.CompletedTask;
                }
            );
            var service = new BotAccountAuthorizationService(
                new ConfiguredBotAccountAuthorizationPolicy(
                    Settings(tokenCachePath),
                    cache,
                    tokenStore,
                    new HelixClient(
                        new RejectingHttpClientFactory(),
                        global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                    ),
                    new UnavailableTokenStatusSource(),
                    new HostedChannelChangeNotifier(events)
                )
            );

            var clearing = service.ClearAsync(CancellationToken.None);
            _ = await tokenStore.DeleteStarted.Reader.ReadAsync(CancellationToken.None);

            clearing.IsCompleted.ShouldBeFalse();
            File.Exists(tokenCachePath).ShouldBeTrue();
            cache.IsCleared.ShouldBeFalse();
            eventCount.ShouldBe(0);
            await tokenStore.ContinueDelete.Writer.WriteAsync(true, CancellationToken.None);
            await clearing;

            File.Exists(tokenCachePath).ShouldBeFalse();
            cache.IsCleared.ShouldBeTrue();
            eventCount.ShouldBe(1);
        }
        finally
        {
            File.Delete(tokenCachePath);
        }
    }

    [Test]
    public async Task ConfiguredPolicy_DurableDeleteFailure_DoesNotReportAuthorizationChange()
    {
        var failure = new IOException("delete failed");
        var cache = new RecordingAccessTokenCache();
        var events = TestEventBus.Create<AppEventKind>();
        var eventCount = 0;
        using var subscription = events.Subscribe(
            AppEventKind.HostedChannelsChanged,
            ObserverIdentity.Named("Test.BotAccountAuthorizationPolicy.Failure"),
            (_, _) =>
            {
                eventCount++;
                return ValueTask.CompletedTask;
            }
        );
        var service = new BotAccountAuthorizationService(
            new ConfiguredBotAccountAuthorizationPolicy(
                Settings("tokens.json"),
                cache,
                new FailingTokenStore(failure),
                new HelixClient(
                    new RejectingHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                new UnavailableTokenStatusSource(),
                new HostedChannelChangeNotifier(events)
            )
        );

        var thrown = await Should.ThrowAsync<IOException>(() =>
            service.ClearAsync(CancellationToken.None)
        );

        thrown.ShouldBeSameAs(failure);
        cache.IsCleared.ShouldBeFalse();
        eventCount.ShouldBe(0);
    }

    [Test]
    public async Task DisabledPolicy_LoadingAndClearingAuthorization_RequiresNoTokenCache()
    {
        var service = new BotAccountAuthorizationService(
            new DisabledBotAccountAuthorizationPolicy(Settings("tokens.json"))
        );

        var status = await service.GetStatusAsync(CancellationToken.None);
        await service.ClearAsync(CancellationToken.None);

        status.State.ShouldBe(BotAccountAuthorizationState.Disabled);
        status.Message.ShouldBe("The Twitch bot runner is not configured.");
    }

    private static BotSettings Settings(string tokenCachePath) =>
        BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost/oauth/callback",
                    Scopes =
                    [
                        Scopes.UserReadModeratedChannels,
                        Scopes.UserReadFollows,
                        Scopes.ModeratorManageAnnouncements,
                    ],
                    TokenCachePath = tokenCachePath,
                },
            }
        );

    private static async Task<BotAccountAuthorizationStatus> LoadConfiguredStatusAsync(
        TokenStatus status
    ) =>
        await ConfiguredService(
                new StaticTokenStatusSource(Result<TokenStatus, TokenStatusError>.Success(status))
            )
            .GetStatusAsync(CancellationToken.None);

    private static async Task<BotAccountAuthorizationStatus> LoadConfiguredStatusAsync(
        TokenStatusError error
    ) =>
        await ConfiguredService(
                new StaticTokenStatusSource(Result<TokenStatus, TokenStatusError>.Error(error))
            )
            .GetStatusAsync(CancellationToken.None);

    private static BotAccountAuthorizationService ConfiguredService(ITokenStatusSource tokenStatus)
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new(
            new ConfiguredBotAccountAuthorizationPolicy(
                Settings("tokens.json"),
                new RecordingAccessTokenCache(),
                new JsonTokenStore(),
                new HelixClient(
                    new CurrentUserHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                tokenStatus,
                new HostedChannelChangeNotifier(events)
            )
        );
    }

    private static ImmutableArray<string> RequiredScopes() =>
        [
            Scopes.UserReadModeratedChannels,
            Scopes.UserReadFollows,
            Scopes.ModeratorManageAnnouncements,
        ];

    private static TokenValidation Validation(IEnumerable<string> scopes) =>
        new("bot-id", "bot", OAuthScopeSet.Create(scopes));

    private sealed class RecordingAccessTokenCache : IAccessTokenCache
    {
        public int ClearCount { get; private set; }

        public bool IsCleared { get; private set; }

        CredentialEpoch IAccessTokenCache.Epoch => default;

        Task<TResult> IAccessTokenCache.ExecuteSynchronizedAsync<TResult>(
            Func<IAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Read-through should not run while clearing.");

        public async Task ClearAsync(
            ITokenStore tokenStore,
            string path,
            CancellationToken cancellationToken
        )
        {
            ClearCount++;
            await tokenStore.DeleteAsync(path, cancellationToken);
            IsCleared = true;
        }
    }

    private sealed class BlockingTokenStore(ITokenStore inner) : ITokenStore
    {
        public Channel<bool> DeleteStarted { get; } = Channel.CreateUnbounded<bool>();

        public Channel<bool> ContinueDelete { get; } = Channel.CreateUnbounded<bool>();

        public Task<Option<TokenSet>> LoadAsync(string path, CancellationToken cancellationToken) =>
            inner.LoadAsync(path, cancellationToken);

        public Task SaveAsync(
            string path,
            TokenSet tokenSet,
            CancellationToken cancellationToken
        ) => inner.SaveAsync(path, tokenSet, cancellationToken);

        public async Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            await DeleteStarted.Writer.WriteAsync(true, cancellationToken);
            _ = await ContinueDelete.Reader.ReadAsync(cancellationToken);
            await inner.DeleteAsync(path, cancellationToken);
        }
    }

    private sealed class FailingTokenStore(Exception failure) : ITokenStore
    {
        public Task<Option<TokenSet>> LoadAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Loading is not expected.");

        public Task SaveAsync(
            string path,
            TokenSet tokenSet,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Saving is not expected.");

        public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            Task.FromException(failure);
    }

    private sealed class StaticTokenStatusSource(Result<TokenStatus, TokenStatusError> result)
        : ITokenStatusSource
    {
        public IO<TokenStatus, TokenStatusError> GetUserAccessTokenStatus(
            IEnumerable<string?> requiredScopes
        ) =>
            IO<TokenStatus, TokenStatusError>.Create(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(result);
            });
    }

    private sealed class CurrentUserHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new CurrentUserHttpMessageHandler());
    }

    private sealed class CurrentUserHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"data":[{"id":"bot-id","login":"bot","display_name":"Bot","profile_image_url":""}]}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RejectingHttpMessageHandler());
    }

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("HTTP should not be requested while clearing.");
    }
}
