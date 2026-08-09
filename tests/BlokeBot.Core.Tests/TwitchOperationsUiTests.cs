using System.Net;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints.Page;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers.Page;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Polls.Page;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Predictions.Page;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TwitchOperationsUiTests
{
    [Test]
    public void RedemptionWaitingAgeUsesExactBandsAndClampsFutureTimestamps()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var presentations = new[]
        {
            RedemptionWaitingAgePresentation.Create(now.AddMinutes(1).UtcDateTime, timeProvider),
            RedemptionWaitingAgePresentation.Create(now.AddSeconds(-119).UtcDateTime, timeProvider),
            RedemptionWaitingAgePresentation.Create(now.AddMinutes(-2).UtcDateTime, timeProvider),
            RedemptionWaitingAgePresentation.Create(now.AddSeconds(-299).UtcDateTime, timeProvider),
            RedemptionWaitingAgePresentation.Create(now.AddMinutes(-5).UtcDateTime, timeProvider),
        };

        presentations
            .Select(static value => value.Band)
            .ShouldBe([
                RedemptionWaitingAgeBand.Fresh,
                RedemptionWaitingAgeBand.Fresh,
                RedemptionWaitingAgeBand.Waiting,
                RedemptionWaitingAgeBand.Waiting,
                RedemptionWaitingAgeBand.NeedsAttention,
            ]);
        presentations[0].Age.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public async Task ChannelPointsRewardEditor_SavesChangedTitle()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(dbFactory, HostFeatureFlags.All);
        var testContext = UiTestContextFactory.CreateWithAuthorization(
            dbFactory,
            host.Id,
            host.Login
        );
        await using var context = testContext.Context;
        ConfigureServices(context, dbFactory);

        var operations = new RecordingChannelPointsOperations(
            new ChannelPointsDashboardState(
                new ChannelPointsAuthorizationReadiness.Ready(),
                [Reward()],
                [],
                []
            )
        );
        _ = context.Services.AddSingleton<IChannelPointsDashboardOperations>(operations);

        var page = context.Render<ChannelPointsPage>();

        page.WaitForElement("[data-channel-point-rewards] button").Click();
        page.Find("#reward-title").Input("Choose a celebration");
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Save reward").Click();

        page.WaitForAssertion(() =>
        {
            _ = operations.UpdatedDraft.ShouldNotBeNull();
            operations.UpdatedDraft!.Title.ShouldBe("Choose a celebration");
        });
    }

    [Test]
    public async Task ClipsRoute_DisabledRecoveryDoesNotExposeAttemptKeysOrDeleteHistory()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(dbFactory, HostFeatureFlags.All);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.TwitchClips.Add(
                new TwitchClip
                {
                    HostId = host.Id,
                    IdempotencyKey = "retained-private-key",
                    Status = TwitchClipStatus.Ambiguous,
                    RequestedAtUtc = DateTime.UtcNow,
                    ResolvedAtUtc = DateTime.UtcNow,
                }
            );
            var persistedHost = await db.Hosts.SingleAsync();
            persistedHost.EnabledFeatures &= ~HostFeatureFlags.NativeTwitchFeatures;
            _ = await db.SaveChangesAsync();
        }

        var unavailableTestContext = UiTestContextFactory.CreateWithAuthorization(
            dbFactory,
            host.Id,
            host.Login
        );
        await using (var context = unavailableTestContext.Context)
        {
            ConfigureServices(context, dbFactory);
            var page = context.Render<ClipsMarkersPage>();

            page.WaitForAssertion(() =>
            {
                _ = page.Find("a[href='/host#chat-tools']");
                page.Markup.ShouldNotContain("retained-private-key");
            });
        }

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task PollsAndPredictionsRoutes_RenderReadyDashboardsAndDisabledRecovery()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(dbFactory, HostFeatureFlags.All);
        var testContext = UiTestContextFactory.CreateWithAuthorization(
            dbFactory,
            host.Id,
            host.Login
        );
        await using var context = testContext.Context;
        ConfigureServices(context, dbFactory);
        _ = context.Services.AddSingleton<IPollDashboardOperations>(
            new StaticPollOperations(
                new PollDashboardState(new PollAuthorizationReadiness.Ready(), null, [], [])
            )
        );
        _ = context.Services.AddSingleton<IPredictionDashboardOperations>(
            new StaticPredictionOperations(
                new PredictionDashboardState(
                    new PredictionAuthorizationReadiness.Ready(),
                    null,
                    [],
                    []
                )
            )
        );

        var polls = context.Render<PollsPage>();
        _ = polls.WaitForElement("#poll-title");

        var predictions = context.Render<PredictionsPage>();
        _ = predictions.WaitForElement("#prediction-title");

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var stored = await db.Hosts.SingleAsync(x => x.Id == host.Id);
            stored.EnabledFeatures =
                HostFeatureFlags.All & ~(HostFeatureFlags.Polls | HostFeatureFlags.Predictions);
            _ = await db.SaveChangesAsync();
        }

        var disabledPolls = context.Render<PollsPage>();
        disabledPolls.WaitForAssertion(() => disabledPolls.FindAll("#poll-title").ShouldBeEmpty());
        var disabledPredictions = context.Render<PredictionsPage>();
        disabledPredictions.WaitForAssertion(() =>
            disabledPredictions.FindAll("#prediction-title").ShouldBeEmpty()
        );
    }

    private sealed class StaticPollOperations(PollDashboardState state) : IPollDashboardOperations
    {
        public Task<PollDashboardState> LoadAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(state);

        public Task<PollOperationOutcome> SaveTemplateAsync(
            int hostId,
            PollTemplateDraft draft,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PollOperationOutcome> StartAsync(
            int hostId,
            int templateId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PollOperationOutcome> EndAsync(
            int hostId,
            bool confirmedExternal,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed class StaticPredictionOperations(PredictionDashboardState state)
        : IPredictionDashboardOperations
    {
        public Task<PredictionDashboardState> LoadAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(state);

        public Task<PredictionOperationOutcome> SaveTemplateAsync(
            int hostId,
            PredictionTemplateDraft draft,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PredictionOperationOutcome> DeleteTemplateAsync(
            int hostId,
            int templateId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PredictionOperationOutcome> StartAsync(
            int hostId,
            int templateId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PredictionOperationOutcome> LockAsync(
            int hostId,
            bool confirmed,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PredictionOperationOutcome> CancelAsync(
            int hostId,
            bool confirmed,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<PredictionOperationOutcome> ResolveAsync(
            int hostId,
            string winningOutcomeId,
            bool confirmed,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private static void ConfigureServices(BunitContext context, SqliteBlokeBotDbFactory dbFactory)
    {
        var events = TestEventBus.Create<AppEventKind>();
        var changes = new HostedChannelChangeNotifier(events);
        var alerts = new DurableAlertService(dbFactory, TimeProvider.System, events);
        var settings = BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );
        var nativeTwitch = new NativeTwitchFeatureGate(dbFactory);
        _ = context.Services.AddSingleton(events);
        _ = context.Services.AddSingleton(changes);
        _ = context.Services.AddSingleton(alerts);
        _ = context.Services.AddSingleton(nativeTwitch);
        _ = context.Services.AddSingleton(
            new ShoutoutService(
                dbFactory,
                null!,
                null!,
                settings,
                events,
                TimeProvider.System,
                nativeTwitch
            )
        );
        _ = context.Services.AddSingleton<IShoutoutDashboardOperations>(static provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        _ = context.Services.AddSingleton(
            new AutomaticRaidShoutoutConfigurationService(dbFactory, TimeProvider.System)
        );
        _ = context.Services.AddSingleton(
            new ClipMarkerService(
                dbFactory,
                new BroadcasterOperationAuthorization(new ReadyBroadcasterProvider(), alerts),
                new HelixClient(
                    new RejectingHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                settings,
                events,
                TimeProvider.System,
                nativeTwitch
            )
        );
        _ = context.Services.AddSingleton<IClipMarkerDashboardOperations>(static provider =>
            provider.GetRequiredService<ClipMarkerService>()
        );
        _ = context.Services.AddSingleton(
            new ModeratorAuthorityService(
                null!,
                new HelixClient(
                    new RejectingHttpClientFactory(),
                    global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
                ),
                settings,
                new HostModAccessService(dbFactory, changes),
                TimeProvider.System
            )
        );
        _ = context.Services.AddSingleton<ToastService>();
    }

    private static async Task<BotHost> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        HostFeatureFlags enabledFeatures
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "streamer-id",
            EnabledFeatures = enabledFeatures,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host;
    }

    private static ChannelPointsRewardView Reward() =>
        new(
            "reward-1",
            "Choose the next emote",
            "Tell us which emote to use.",
            500,
            true,
            true,
            false,
            true,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            "#9147FF"
        );

    private sealed class RecordingChannelPointsOperations(ChannelPointsDashboardState state)
        : IChannelPointsDashboardOperations
    {
        public ChannelPointsRewardDraft? UpdatedDraft { get; private set; }

        public Task<ChannelPointsDashboardState> LoadAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(state);

        public Task<ChannelPointsOperationOutcome> CreateRewardAsync(
            int hostId,
            ChannelPointsRewardDraft draft,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RewardCreated(Reward())
            );

        public Task<ChannelPointsOperationOutcome> UpdateRewardAsync(
            int hostId,
            string rewardId,
            ChannelPointsRewardDraft draft,
            bool isEnabled,
            bool paused,
            CancellationToken cancellationToken
        )
        {
            UpdatedDraft = draft;
            return Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RewardUpdated()
            );
        }

        public Task<ChannelPointsOperationOutcome> DeleteRewardAsync(
            int hostId,
            string rewardId,
            bool confirmed,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RewardDeleted()
            );

        public Task<ChannelPointsOperationOutcome> UpdateRedemptionAsync(
            int hostId,
            string redemptionId,
            bool fulfill,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ChannelPointsOperationOutcome>(
                new ChannelPointsOperationOutcome.RedemptionUpdated()
            );
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        ) =>
            Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        "streamer-id",
                        "streamer",
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
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
    }

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RejectingHandler());

        private sealed class RejectingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
