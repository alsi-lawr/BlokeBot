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
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class NativeTwitchFeatureChangeObserverTests
{
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
            new PollService(
                database,
                new BroadcasterOperationAuthorization(broadcasters, alerts),
                helix,
                settings,
                events,
                gate
            ),
            new ClipMarkerService(
                database,
                new BroadcasterOperationAuthorization(broadcasters, alerts),
                helix,
                settings,
                events,
                TimeProvider.System,
                gate
            ),
            new ChannelPointsService(
                database,
                new BroadcasterOperationAuthorization(broadcasters, alerts),
                helix,
                settings,
                events,
                TimeProvider.System,
                gate
            ),
            new PredictionService(
                database,
                new BroadcasterOperationAuthorization(broadcasters, alerts),
                helix,
                settings,
                events,
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
