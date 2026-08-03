using System.Collections.Immutable;
using System.Net;
using System.Text;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class NativeTwitchFeatureChangeObserverTests
{
    [Test]
    public async Task ProductionComposition_MapsDashboardAndObserverContractsToEstablishedSingletons()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var events = TestEventBus.Create<AppEventKind>();
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton<IHostBroadcasterTokenStatusProvider>(new ReadyBroadcaster());
        _ = services.AddSingleton<IHostBotAccountTokenStatusProvider>(new ReadyBotAccount());
        _ = services.AddSingleton(
            new HelixClient(
                new SingleHandlerFactory(new NativeReconciliationHandler()),
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            )
        );
        _ = services.AddSingleton(
            BotSettings.FromOptions(
                new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
            )
        );
        _ = services.AddSingleton(events);
        _ = services.AddSingleton(new DurableAlertService(database, TimeProvider.System, events));
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddBlokeBotTwitchOperations();
        await using var provider = services.BuildServiceProvider();

        var shoutouts = provider.GetRequiredService<ShoutoutService>();
        var polls = provider.GetRequiredService<PollService>();
        var clipsMarkers = provider.GetRequiredService<ClipMarkerService>();
        var channelPoints = provider.GetRequiredService<ChannelPointsService>();
        var predictions = provider.GetRequiredService<PredictionService>();

        provider.GetRequiredService<IShoutoutDashboardOperations>().ShouldBeSameAs(shoutouts);
        provider.GetRequiredService<IPollDashboardOperations>().ShouldBeSameAs(polls);
        provider.GetRequiredService<IClipMarkerDashboardOperations>().ShouldBeSameAs(clipsMarkers);
        provider
            .GetRequiredService<IChannelPointsDashboardOperations>()
            .ShouldBeSameAs(channelPoints);
        provider.GetRequiredService<IPredictionDashboardOperations>().ShouldBeSameAs(predictions);

        provider.GetRequiredService<IShoutoutEventObserver>().ShouldBeSameAs(shoutouts);
        provider.GetRequiredService<IPollEventObserver>().ShouldBeSameAs(polls);
        provider.GetRequiredService<IChannelPointsEventObserver>().ShouldBeSameAs(channelPoints);
        provider.GetRequiredService<IPredictionEventObserver>().ShouldBeSameAs(predictions);
        provider
            .GetRequiredService<IAutomaticRaidNativeShoutoutOperation>()
            .ShouldBeSameAs(shoutouts);

        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(AutomaticRaidShoutoutConfigurationService)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(AutomaticRaidShoutoutObserver)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(IAutomaticRaidShoutoutDelivery)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(IIncomingRaidEventObserver)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
    }

    [Test]
    public async Task Reenable_ReconcilesRestoredServices_AndRegistersTheirRuntimeObservers()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    EnabledFeatures = HostFeatureFlags.All,
                    Login = "channel",
                    DisplayName = "Channel",
                    TwitchUserId = "channel-id",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var events = TestEventBus.Create<AppEventKind>();
        var handler = new NativeReconciliationHandler();
        var helix = new HelixClient(
            new SingleHandlerFactory(handler),
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var settings = BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );
        var broadcasters = new ReadyBroadcaster();
        var alerts = new DurableAlertService(database, TimeProvider.System, events);
        var gate = new NativeTwitchFeatureGate(database);
        var observer = new NativeTwitchFeatureChangeObserver(
            new EventSubChannelReconciliationTrigger(null!),
            new PollService(database, broadcasters, helix, settings, events, alerts, gate),
            new ClipMarkerService(
                database,
                broadcasters,
                helix,
                settings,
                events,
                alerts,
                TimeProvider.System,
                gate
            ),
            new ChannelPointsService(
                database,
                broadcasters,
                helix,
                settings,
                events,
                alerts,
                TimeProvider.System,
                gate
            ),
            new PredictionService(
                database,
                broadcasters,
                helix,
                settings,
                events,
                alerts,
                NullLogger<PredictionService>.Instance,
                gate
            )
        );

        await observer.NativeTwitchFeatureChangedAsync(
            1,
            HostFeatureFlags.Polls,
            NativeTwitchFeatureState.Enabled,
            CancellationToken.None
        );
        await observer.NativeTwitchFeatureChangedAsync(
            1,
            HostFeatureFlags.RewardsAndRedemptions,
            NativeTwitchFeatureState.Enabled,
            CancellationToken.None
        );
        await observer.NativeTwitchFeatureChangedAsync(
            1,
            HostFeatureFlags.Predictions,
            NativeTwitchFeatureState.Enabled,
            CancellationToken.None
        );

        handler.Paths.ShouldContain(static path =>
            path.EndsWith("/helix/polls", StringComparison.Ordinal)
        );
        handler
            .Paths.Count(static path =>
                path.EndsWith("/helix/channel_points/custom_rewards", StringComparison.Ordinal)
            )
            .ShouldBe(2);
        handler.Paths.ShouldContain(static path =>
            path.EndsWith("/helix/predictions", StringComparison.Ordinal)
        );

        var services = new ServiceCollection();
        _ = services.AddBlokeBotTwitchOperations();
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(ChannelPointsService)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(PredictionService)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(IChannelPointsEventObserver)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
        services.ShouldContain(static descriptor =>
            descriptor.ServiceType == typeof(IPredictionEventObserver)
            && descriptor.Lifetime == ServiceLifetime.Singleton
        );
    }

    private sealed class ReadyBroadcaster : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "token",
                    new TokenValidation(
                        "channel-id",
                        "channel",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes],
                    [.. HostBroadcasterAuthorizationService.MilestoneScopes]
                )
            );

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(static _ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount("channel", "token")
                    )
                )
            );
    }

    private sealed class ReadyBotAccount : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            var scopes = requiredScopes.OfType<string>().ToImmutableArray();
            return Task.FromResult(
                new ActiveBotAccountTokenStatus
                {
                    BotLogin = "bot",
                    Status = new TokenStatus.Ready(
                        "token",
                        new TokenValidation("bot-id", "bot", OAuthScopeSet.Create(scopes)),
                        scopes,
                        scopes
                    ),
                }
            );
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class NativeReconciliationHandler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath.EndsWith(
                "/predictions",
                StringComparison.Ordinal
            )
                ? """{"data":[],"pagination":{}}"""
                : """{"data":[]}""";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
