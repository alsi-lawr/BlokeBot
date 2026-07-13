using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

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
    public async Task InvalidToken_LoadingConfiguredStatus_ReportsNotAuthorized()
    {
        var status = await LoadConfiguredStatusAsync(new TokenStatus.Invalid(RequiredScopes()));

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
    public async Task ConfiguredPolicy_ClearingAuthorization_DeletesTokenAndClearsRequiredCache()
    {
        var tokenCachePath = Path.Combine(
            Path.GetTempPath(),
            $"blokebot-cs004-{Guid.NewGuid():N}.json"
        );
        await File.WriteAllTextAsync(tokenCachePath, "token state");
        try
        {
            var cache = new RecordingAccessTokenCache();
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
                    new HelixClient(new RejectingHttpClientFactory()),
                    new UnavailableTokenStatusSource(),
                    new HostedChannelChangeNotifier(events)
                )
            );

            await service.ClearAsync(CancellationToken.None);

            File.Exists(tokenCachePath).ShouldBeFalse();
            cache.ClearCount.ShouldBe(1);
            eventCount.ShouldBe(1);
        }
        finally
        {
            File.Delete(tokenCachePath);
        }
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

    private static TwitchBotSettings Settings(string tokenCachePath)
    {
        return TwitchBotSettings.FromOptions(
            new TwitchBotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost/oauth/callback",
                    Scopes = [Scopes.UserReadModeratedChannels],
                    TokenCachePath = tokenCachePath,
                },
            }
        );
    }

    private static async Task<BotAccountAuthorizationStatus> LoadConfiguredStatusAsync(
        TokenStatus status
    )
    {
        return await ConfiguredService(
                new StaticTokenStatusSource(Result<TokenStatus, TokenStatusError>.Success(status))
            )
            .GetStatusAsync(CancellationToken.None);
    }

    private static async Task<BotAccountAuthorizationStatus> LoadConfiguredStatusAsync(
        TokenStatusError error
    )
    {
        return await ConfiguredService(
                new StaticTokenStatusSource(Result<TokenStatus, TokenStatusError>.Error(error))
            )
            .GetStatusAsync(CancellationToken.None);
    }

    private static BotAccountAuthorizationService ConfiguredService(ITokenStatusSource tokenStatus)
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new(
            new ConfiguredBotAccountAuthorizationPolicy(
                Settings("tokens.json"),
                new RecordingAccessTokenCache(),
                new HelixClient(new CurrentUserHttpClientFactory()),
                tokenStatus,
                new HostedChannelChangeNotifier(events)
            )
        );
    }

    private static ImmutableArray<string> RequiredScopes()
    {
        return [Scopes.UserReadModeratedChannels];
    }

    private static TokenValidation Validation(IEnumerable<string> scopes)
    {
        return new("bot-id", "bot", scopes.ToHashSet(StringComparer.Ordinal));
    }

    private sealed class RecordingAccessTokenCache : IAccessTokenCache
    {
        public int ClearCount { get; private set; }

        Task<TResult> IAccessTokenCache.ExecuteSynchronizedAsync<TResult>(
            Func<IAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("Read-through should not run while clearing.");
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StaticTokenStatusSource(Result<TokenStatus, TokenStatusError> result)
        : ITokenStatusSource
    {
        public IO<TokenStatus, TokenStatusError> GetUserAccessTokenStatus(
            IEnumerable<string?> requiredScopes
        )
        {
            return IO<TokenStatus, TokenStatusError>.Create(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(result);
            });
        }
    }

    private sealed class CurrentUserHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new CurrentUserHttpMessageHandler());
        }
    }

    private sealed class CurrentUserHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
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
    }

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new RejectingHttpMessageHandler());
        }
    }

    private sealed class RejectingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("HTTP should not be requested while clearing.");
        }
    }
}
