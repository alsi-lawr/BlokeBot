using System.Net;
using System.Text;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class HostedChannelRuntimeStatusTests
{
    [Test]
    public async Task HostedChannelWithLocalRuntimeState_LoadingSummary_AvoidsRemoteBotCheck()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var httpClientFactory = new CountingHttpClientFactory();
        var options = BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client",
                    ClientSecret = "secret",
                    Scopes = [Scopes.UserReadModeratedChannels],
                },
            }
        );
        var helix = new HelixClient(
            httpClientFactory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var service = new HostedChannelRuntimeStatusService(
            dbFactory,
            ChannelAuthorizationService(dbFactory, "channel:bot"),
            new HostBotStatusService(
                new UnavailableHostBotAppAccessTokenSource(),
                new RejectingHostBotAccountTokenStatusProvider(),
                helix,
                options
            )
        );

        var summary = (
            await service.LoadHostRuntimeSummary(hostId).RunAsync(CancellationToken.None)
        ).Match<HostedChannelRuntimeSummary?>(value => value, () => null);

        _ = summary.ShouldNotBeNull();
        summary!.IsChannelBotAuthorized.ShouldBeTrue();
        summary.ChannelBotAuthorizationScopesCurrent.ShouldBeTrue();
        _ = summary.Lifecycle.ShouldBeOfType<HostedChannelRuntimeLifecycle.Started>();
        httpClientFactory.RequestCount.ShouldBe(0);
    }

    private static ChannelBotAuthorizationService ChannelAuthorizationService(
        SqliteBlokeBotDbFactory dbFactory,
        params string[] scopes
    ) =>
        new(
            dbFactory,
            new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
            ChannelOAuthService(scopes)
        );

    private static ChannelBotOAuthService ChannelOAuthService(params string[] scopes)
    {
        var values = new Dictionary<string, string?>
        {
            ["TwitchBot:Identity:ClientId"] = "client",
            ["TwitchBot:Identity:ClientSecret"] = "secret",
        };

        for (var i = 0; i < scopes.Length; i++)
        {
            values[$"TwitchBot:ChannelAuthorization:Scopes:{i}"] = scopes[i];
        }

        return new(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new OAuthTransport(
                new CountingHttpClientFactory(),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = "streamer",
            DisplayName = "Streamer",
            ChannelBotAuthorizedAtUtc = DateTime.UtcNow,
            ChannelBotAuthorizedScopes = "channel:bot",
            BotRuntimeState = BotChannelRuntimeState.Started,
            BotRuntimeStateChangedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class RejectingHostBotAccountTokenStatusProvider
        : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Remote bot status should not be queried.");
    }

    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        public int RequestCount { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(CountingHttpClientFactory owner) : HttpMessageHandler
        {
            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"user_id":"bot-id","login":"bot","scopes":["user:read:moderated_channels"]}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }
        }
    }
}
