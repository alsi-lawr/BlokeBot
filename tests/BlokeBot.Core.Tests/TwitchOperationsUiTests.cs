using System.Collections.Immutable;
using System.Net;
using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints.Page;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers.Page;
using BlokeBot.Core.Features.TwitchOperations.Polls.Page;
using BlokeBot.Core.Features.TwitchOperations.Predictions.Page;
using BlokeBot.Core.Features.TwitchOperations.Shared;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class TwitchOperationsUiTests
{
    [Test]
    public void NativeSwitcherExposesFiveLinksAndSharedAtRestCurrentHoverAndFocusHooks()
    {
        using var context = new BunitContext();
        context
            .Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/twitch-operations/polls");

        var switcher = context.Render<NativeTwitchToolSwitcher>();

        switcher.FindAll("nav[aria-label='Native Twitch tools'] a").Count.ShouldBe(5);
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
            page.Find("[data-native-route='shoutouts']");
            page.Find("[data-native-route='shoutouts']")
                .ClassList.ShouldContain("dashboard-page--readable");
            page.Find("#shoutout-target");
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
                page.Find("[data-native-route='clips-markers']");
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
            db.TwitchClips.Add(
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
            persistedHost.EnabledFeatures &= ~HostFeatureFlags.NativeTwitch;
            await db.SaveChangesAsync();
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
                page.Markup.ShouldContain("This Twitch tool is turned off");
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

    private static void ConfigureServices(BunitContext context, SqliteBlokeBotDbFactory dbFactory)
    {
        var events = TestEventBus.Create<AppEventKind>();
        var changes = new HostedChannelChangeNotifier(events);
        var alerts = new DurableAlertService(dbFactory, TimeProvider.System, events);
        var settings = BotSettings.FromOptions(
            new BotOptions { Identity = new BotIdentityOptions { ClientId = "client" } }
        );
        var nativeTwitch = new NativeTwitchFeatureGate(dbFactory);
        context.Services.AddSingleton(events);
        context.Services.AddSingleton(changes);
        context.Services.AddSingleton(alerts);
        context.Services.AddSingleton(nativeTwitch);
        context.Services.AddSingleton(
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
        context.Services.AddSingleton<IShoutoutDashboardOperations>(provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        context.Services.AddSingleton(
            new AutomaticRaidShoutoutConfigurationService(dbFactory, TimeProvider.System)
        );
        context.Services.AddSingleton(
            new ClipMarkerService(
                dbFactory,
                new ReadyBroadcasterProvider(),
                new HelixClient(new RejectingHttpClientFactory()),
                settings,
                events,
                alerts,
                TimeProvider.System,
                nativeTwitch
            )
        );
        context.Services.AddSingleton<IClipMarkerDashboardOperations>(provider =>
            provider.GetRequiredService<ClipMarkerService>()
        );
        context.Services.AddSingleton(
            new ModeratorAuthorityService(
                null!,
                new HelixClient(new RejectingHttpClientFactory()),
                settings,
                new HostModAccessService(dbFactory, changes),
                TimeProvider.System
            )
        );
        context.Services.AddSingleton<ToastService>();
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
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host;
    }

    private sealed class ReadyBroadcasterProvider : IHostBroadcasterTokenStatusProvider
    {
        public Task<TokenStatus> GetTokenStatusAsync(
            int hostId,
            IEnumerable<string?> requiredScopes,
            CancellationToken ct
        )
        {
            return Task.FromResult<TokenStatus>(
                new TokenStatus.Ready(
                    "broadcaster-token",
                    new TokenValidation(
                        "streamer-id",
                        "streamer",
                        OAuthScopeSet.Create(HostBroadcasterAuthorizationService.MilestoneScopes)
                    ),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes),
                    ImmutableArray.CreateRange(HostBroadcasterAuthorizationService.MilestoneScopes)
                )
            );
        }

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        )
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    )
                )
            );
        }
    }

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new RejectingHandler());
        }

        private sealed class RejectingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
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
