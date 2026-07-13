using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class BotAccountAuthorizationPolicyTests
{
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
            BotAccountTokenStatusResolver tokenStatus = (_, _) =>
                throw new InvalidOperationException("Status should not be queried while clearing.");
            var service = new BotAccountAuthorizationService(
                new ConfiguredBotAccountAuthorizationPolicy(
                    Settings(tokenCachePath),
                    cache,
                    new TwitchHelixApiClient(new RejectingHttpClientFactory()),
                    tokenStatus,
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
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://localhost/oauth/callback",
                    Scopes = [TwitchScopes.UserReadModeratedChannels],
                    TokenCachePath = tokenCachePath,
                },
            }
        );
    }

    private sealed class RecordingAccessTokenCache : ITwitchAccessTokenCache
    {
        public int ClearCount { get; private set; }

        Task<TResult> ITwitchAccessTokenCache.ExecuteSynchronizedAsync<TResult>(
            Func<ITwitchAccessTokenCacheTransaction, CancellationToken, Task<TResult>> operation,
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
