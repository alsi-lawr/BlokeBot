using System.Net;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels;
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
using BlokeBot.Core.Features.TwitchOperations.Shared;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class TwitchOperationsUiTests
{
    [Test]
    public async Task NativeSwitcherExposesOnlyEnabledLinksAndSharedAtRestCurrentHoverAndFocusHooks()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.Hosts.Add(
                new BotHost
                {
                    TwitchUserId = "host-id",
                    Login = "host",
                    DisplayName = "Host",
                    EnabledFeatures =
                        HostFeatureFlags.Shoutouts
                        | HostFeatureFlags.Polls
                        | HostFeatureFlags.Predictions,
                    CreatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await db.SaveChangesAsync();
        }

        using var context = new BunitContext();
        _ = context.Services.AddSingleton(
            new HostFeatureService(
                dbFactory,
                new HostedChannelChangeNotifier(TestEventBus.Create<AppEventKind>()),
                []
            )
        );
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/twitch-operations/polls");

        var switcher = context.Render<NativeTwitchToolSwitcher>(parameters =>
            parameters.Add(component => component.HostId, 1)
        );

        switcher.WaitForAssertion(() =>
        {
            switcher.FindAll("nav[aria-label='Native Twitch tools'] a").Count.ShouldBe(3);
            switcher.Markup.ShouldNotContain("Clips &amp; markers");
            switcher.Markup.ShouldNotContain("Rewards &amp; redemptions");
        });
        var links = switcher.FindAll(".native-tool-switcher__link");
        links.ShouldAllBe(link => link.TextContent.Trim().Length > 0);
        links
            .Single(link => link.GetAttribute("aria-current") == "page")
            .TextContent.ShouldContain("Polls");

        var styles = ReadRepositoryFile(
            "src",
            "BlokeBot.Core",
            "Styles",
            "features",
            "native-twitch.css"
        );
        styles.ShouldContain("background: var(--app-control-bg)");
        styles.ShouldContain("align-items: center");
        styles.ShouldContain("justify-content: center");
        styles.ShouldContain("min-height: 3rem");
        styles.ShouldContain(".native-tool-switcher__link:hover");
        styles.ShouldContain(".native-tool-switcher__link:focus-visible");
        styles.ShouldContain(".native-tool-switcher__link--current");
        styles.ShouldContain("flex-wrap: wrap");
    }

    [Test]
    public void NativeDashboardDisclosuresUseFiniteCardCollections()
    {
        var styles = ReadRepositoryFile(
            "src",
            "BlokeBot.Core",
            "Styles",
            "features",
            "native-twitch.css"
        );
        styles.ShouldNotContain(".dashboard-page[data-native-route] {");

        styles
            .TrimEnd()
            .ShouldEndWith(
                """
                .dashboard-page[data-native-route] .phone-card-list {
                    display: grid;
                }
                """
            );

        var sharedPage = ReadRepositoryFile(
            "src",
            "BlokeBot.Core",
            "Components",
            "Layout",
            "DashboardPage.razor"
        );
        sharedPage.ShouldNotContain("application-card-collection dashboard-page");

        var sharedStyles = ReadRepositoryFile(
            "src",
            "BlokeBot.Core",
            "Styles",
            "components",
            "application-card-layout.css"
        );
        sharedStyles.ShouldContain("--app-card-clearance: 12px");
        sharedStyles.ShouldContain("gap: var(--app-card-clearance)");

        foreach (
            var path in new[]
            {
                new[] { "Shoutouts", "ShoutoutsPage.razor" },
                ["Polls", "Page", "PollsPage.razor"],
                ["ClipsMarkers", "Page", "ClipsMarkersPage.razor"],
                ["ChannelPoints", "Page", "ChannelPointsPage.razor"],
                ["Predictions", "Page", "PredictionsPage.razor"],
            }
        )
        {
            var page = ReadRepositoryFile([
                "src",
                "BlokeBot.Core",
                "Features",
                "TwitchOperations",
                .. path,
            ]);
            page.ShouldContain("data-native-route=");
            page.ShouldContain("Width=\"DashboardPageWidth.Wide\"");
            page.ShouldContain(
                """<ApplicationCardCollection Owner="native-dashboard-disclosure-cards">"""
            );
        }
    }

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
        presentations
            .Select(static value => value.SemanticValue)
            .ShouldBe(["fresh", "fresh", "waiting", "waiting", "needs-attention"]);
        presentations[0].Age.ShouldBe(TimeSpan.Zero);
        presentations[0].Label.ShouldBe("New · waiting less than a minute");
        presentations[2].Label.ShouldBe("Waiting · 2 minutes");
        presentations[4].Label.ShouldBe("Needs attention · waiting 5 minutes");
        presentations[0].BadgeClass.ShouldContain("sky");
        presentations[2].BadgeClass.ShouldContain("amber");
        presentations[4].BadgeClass.ShouldContain("rose");
    }

    [Test]
    public async Task ChannelPointsReadyStateOrdersWorkAndKeepsWaitingAgeAndEditingUsable()
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

        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingChannelPointsOperations(
            new ChannelPointsDashboardState(
                new ChannelPointsAuthorizationReadiness.Ready(),
                [Reward()],
                [
                    Redemption("future", now.AddMinutes(1).UtcDateTime),
                    Redemption("fresh", now.AddSeconds(-119).UtcDateTime),
                    Redemption("waiting", now.AddMinutes(-2).UtcDateTime),
                    Redemption("still-waiting", now.AddSeconds(-299).UtcDateTime),
                    Redemption("needs-attention", now.AddMinutes(-5).UtcDateTime),
                ],
                []
            )
        );
        _ = context.Services.AddSingleton<IChannelPointsDashboardOperations>(operations);

        var page = context.Render<ChannelPointsPage>();

        page.WaitForAssertion(() =>
        {
            SectionTitles(page)
                .ShouldBe([
                    "Unfulfilled redemptions",
                    "Rewards",
                    "Create a reward",
                    "Redemption history",
                ]);
            var cards = page.FindAll("[data-redemption-waiting-age]");
            cards.Count.ShouldBe(5);
            cards
                .Select(card => card.GetAttribute("data-redemption-waiting-age"))
                .ShouldBe(["fresh", "fresh", "waiting", "waiting", "needs-attention"]);
            foreach (var card in cards)
            {
                var status = card.QuerySelector("[data-waiting-age-band][role='status']");
                _ = status.ShouldNotBeNull();
                status!.TextContent.ShouldContain("minute");
                status.GetAttribute("aria-label").ShouldStartWith("Redemption ");
            }
        });

        page.Find("[data-channel-point-rewards] button").Click();
        SectionTitles(page)
            .ShouldBe(["Unfulfilled redemptions", "Rewards", "Edit reward", "Redemption history"]);
        page.Find("#reward-title").GetAttribute("value").ShouldBe("Choose the next emote");
        page.Find("#reward-title").Input("Choose a celebration");
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Save reward").Click();

        page.WaitForAssertion(() =>
        {
            _ = operations.UpdatedDraft.ShouldNotBeNull();
            operations.UpdatedDraft!.Title.ShouldBe("Choose a celebration");
            SectionTitles(page)
                .ShouldBe([
                    "Unfulfilled redemptions",
                    "Rewards",
                    "Create a reward",
                    "Redemption history",
                ]);
        });
    }

    [Test]
    public async Task ShoutoutsRoute_KeepsManualTaskThenAutomaticSettingsThenNativeHistory()
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

        var page = context.Render<ShoutoutsPage>();

        page.WaitForAssertion(() =>
        {
            _ = page.Find("[data-native-route='shoutouts']");
            page.Find("[data-native-route='shoutouts']")
                .ClassList.ShouldContain("dashboard-page--wide");
            _ = page.Find("#shoutout-target");
            var sections = page.FindAll(".disclosure-title")
                .Select(element => element.TextContent.Trim())
                .ToArray();
            sections.ShouldBe(["Automatic raid shoutouts", "Recent shoutouts"]);
            page.Find("nav[aria-label='Native Twitch tools']")
                .QuerySelectorAll("a")
                .Length.ShouldBe(5);
            page.FindAll("#poll-title, #reward-title, #prediction-title").ShouldBeEmpty();
        });
    }

    [Test]
    public async Task ClipsRoute_ReadyAndUnavailable_AreFocusedDirectAndDoNotExposeAttemptKeys()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(dbFactory, HostFeatureFlags.All);
        var readyTestContext = UiTestContextFactory.CreateWithAuthorization(
            dbFactory,
            host.Id,
            host.Login
        );
        await using (var context = readyTestContext.Context)
        {
            ConfigureServices(context, dbFactory);
            context
                .Services.GetRequiredService<NavigationManager>()
                .NavigateTo("/twitch-operations/clips-markers");

            var page = context.Render<ClipsMarkersPage>();

            page.WaitForAssertion(() =>
            {
                _ = page.Find("[data-native-route='clips-markers']");
                var switcher = page.Find("nav[aria-label='Native Twitch tools']");
                switcher.QuerySelectorAll("a").Length.ShouldBe(5);
                var currentLink = switcher.QuerySelector(
                    "a[href='twitch-operations/clips-markers']"
                )!;
                currentLink.ClassList.ShouldContain("native-tool-switcher__link--current");
                currentLink.GetAttribute("aria-current").ShouldBe("page");
                page.Find("button").TextContent.ShouldContain("Create clip");
                page.FindAll("[data-native-route]").Count.ShouldBe(1);
                page.FindAll("#shoutout-target, #poll-title, #reward-title, #prediction-title")
                    .ShouldBeEmpty();
                page.Markup.ShouldNotContain("IdempotencyKey", Case.Insensitive);
                page.Markup.ShouldNotContain("request key", Case.Insensitive);
                page.Markup.ShouldNotContain("stable key", Case.Insensitive);
            });
        }

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
                page.Find(".page-state__title")
                    .TextContent.ShouldContain("Clips & markers is turned off");
                page.Find("a[href='/host#chat-tools']")
                    .TextContent.ShouldContain("Open Channel setup");
                page.Markup.ShouldNotContain("Create clip");
                page.Markup.ShouldNotContain("retained-private-key");
            });
        }

        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.TwitchClips.CountAsync()).ShouldBe(1);
        typeof(ClipsMarkersPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Select(attribute => attribute.Template)
            .ShouldContain("/twitch-operations/clips-markers");
        new[]
        {
            (Type: typeof(ShoutoutsPage), Route: "/twitch-operations/shoutouts"),
            (Type: typeof(PollsPage), Route: "/twitch-operations/polls"),
            (Type: typeof(ClipsMarkersPage), Route: "/twitch-operations/clips-markers"),
            (Type: typeof(ChannelPointsPage), Route: "/twitch-operations/channel-points"),
            (Type: typeof(PredictionsPage), Route: "/twitch-operations/predictions"),
        }.ShouldAllBe(route =>
            route
                .Type.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>()
                .Any(attribute => attribute.Template == route.Route)
        );
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
        polls.WaitForAssertion(() =>
        {
            _ = polls.Find("[data-native-route='polls']");
            _ = polls.Find("#poll-title");
        });

        var predictions = context.Render<PredictionsPage>();
        predictions.WaitForAssertion(() =>
        {
            _ = predictions.Find("[data-native-route='predictions']");
            _ = predictions.Find("#prediction-title");
        });

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var stored = await db.Hosts.SingleAsync(x => x.Id == host.Id);
            stored.EnabledFeatures =
                HostFeatureFlags.All & ~(HostFeatureFlags.Polls | HostFeatureFlags.Predictions);
            _ = await db.SaveChangesAsync();
        }

        var disabledPolls = context.Render<PollsPage>();
        disabledPolls.WaitForAssertion(() =>
        {
            disabledPolls.Markup.ShouldContain("Polls is turned off");
            disabledPolls.FindAll("#poll-title").ShouldBeEmpty();
        });
        var disabledPredictions = context.Render<PredictionsPage>();
        disabledPredictions.WaitForAssertion(() =>
        {
            disabledPredictions.Markup.ShouldContain("Predictions is turned off");
            disabledPredictions.FindAll("#prediction-title").ShouldBeEmpty();
        });
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

    private static string[] SectionTitles(IRenderedComponent<ChannelPointsPage> page) =>
        page.FindAll(".disclosure-title, .task-panel__title")
            .Select(static element => element.TextContent.Trim())
            .ToArray();

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

    private static ChannelPointsRedemptionView Redemption(
        string providerRedemptionId,
        DateTime redeemedAtUtc
    ) =>
        new(
            providerRedemptionId,
            "reward-1",
            "Choose the next emote",
            "viewer",
            "PartyParrot",
            "Unfulfilled",
            redeemedAtUtc,
            redeemedAtUtc,
            true
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

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(relativePath)}'."
        );
    }
}
