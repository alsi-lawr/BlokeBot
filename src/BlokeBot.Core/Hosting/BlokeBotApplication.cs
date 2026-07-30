using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Users;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.BotStatus;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.History;
using BlokeBot.Core.Features.Guessing.HostSetup;
using BlokeBot.Core.Features.Guessing.Rounds;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.HostSetup;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Twitch;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static class BlokeBotApplication
{
    public static WebApplicationBuilder AddBlokeBotCore(
        this WebApplicationBuilder builder,
        BlokeBotRuntimeMode runtime
    )
    {
        BlokeBotLogging.Configure(builder.Logging);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(BlokeBotBuildIdentity.Current);
        builder.Services.AddSingleton<UiFaultTelemetry>();

        builder
            .Services.AddOptions<BlokeBotOptions>()
            .BindConfiguration("BlokeBot")
            .Validate(BlokeBotOptionsValidation.IsValid, "BlokeBot options are invalid.")
            .ValidateOnStart();
        builder
            .Services.AddOptions<WebAuthOptions>()
            .BindConfiguration("TwitchWebAuth")
            .ValidateOnStart();
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        var twitchEndpoints =
            builder
                .Configuration.GetSection(TwitchEndpointPolicy.ConfigurationSectionName)
                .Get<TwitchEndpointPolicy>()
            ?? new TwitchEndpointPolicy();
        twitchEndpoints.Validate();
        builder.Services.AddSingleton(twitchEndpoints);

        var online = runtime == BlokeBotRuntimeMode.Online;
        builder.Services.AddEventBus<AppEventKind>(
            ObserverBoundary.Named("BlokeBot.ApplicationEvents"),
            eventKind => ObserverEventIdentity.Named($"BlokeBot.{eventKind}")
        );
        builder
            .Services.AddBlokeBotAppCommands()
            .AddBlokeBotPublicChat()
            .AddBlokeBotAlerts()
            .AddBlokeBotCustomCommands(
                online
                    ? CustomAnnouncementDeliveryMode.PublicChat
                    : CustomAnnouncementDeliveryMode.Disabled
            )
            .AddBlokeBotSiteAccess(
                online
                    ? AccessListProfileEnrichmentMode.Twitch
                    : AccessListProfileEnrichmentMode.Disabled
            )
            .AddBlokeBotAdmin(
                online ? BotAccountAuthorizationMode.Twitch : BotAccountAuthorizationMode.Disabled
            )
            .AddBlokeBotHostedChannels(
                online ? HostBotAppAccessTokenMode.Twitch : HostBotAppAccessTokenMode.Unavailable
            )
            .AddBlokeBotHosts()
            .AddBlokeBotGuessing()
            .AddBlokeBotPoints(
                online
                    ? PointsGiveawayNotificationMode.PublicChat
                    : PointsGiveawayNotificationMode.ReplyOnly
            )
            .AddBlokeBotRequestBoards()
            .AddBlokeBotPlayWithViewers()
            .AddBlokeBotToasts()
            .AddBlokeBotTwitchOperations()
            .AddBlokeBotAuth();
        builder.Services.AddOAuthTransport();
        builder.Services.AddHelix();
        builder.Services.AddHttpClient();
        AddAuthentication(builder);

        var botSection = builder.Configuration.GetSection("TwitchBot");
        if (online)
        {
            builder
                .Services.AddTwitchBot(botSection)
                .UseBlokeBotHostedChannelProvider()
                .UseWhisperCommandResponseSender()
                .UseBlokeBotHostedChannelLifecycleNotifier()
                .AddCommandModule<CommandStrategyModule<GuessCommandKind, AppCommandRouteState>>()
                .AddCommandModule<CommandStrategyModule<PointsCommandKind, AppCommandRouteState>>()
                .AddCommandModule<RequestBoardCommandModule>()
                .AddCommandModule<PlayQueueCommandModule>()
                .AddCommandModule<CustomCommandModule>();
        }
        else
        {
            builder.Services.AddTwitchBotSettings(botSection);
            builder.Services.AddUnavailableAccessTokenProvider();
            builder.Services.AddOfflineBotRuntimeStatus();
            builder.Services.Replace(
                ServiceDescriptor.Singleton<IPointTargetUserLookup, OfflinePointTargetUserLookup>()
            );
            builder.Services.Replace(
                ServiceDescriptor.Singleton<
                    IPublicChatMessageSender,
                    OfflinePublicChatMessageSender
                >()
            );
        }

        return builder;
    }

    public static async Task InitializeBlokeBotPersistenceAsync(
        this WebApplication app,
        CancellationToken cancellationToken
    )
    {
        await app
            .Services.GetRequiredService<BlokeBotDatabaseInitializer>()
            .InitializeAsync(cancellationToken);
    }

    public static WebApplication UseBlokeBotCore(
        this WebApplication app,
        BlokeBotRuntimeMode runtime
    )
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseAntiforgery();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMethods(
            "/favicon.ico",
            ["GET", "HEAD"],
            () => Results.Redirect("/blokebot-mark.svg")
        );
        app.UseStaticFiles();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();
        app.MapAuthEndpoints();
        if (runtime == BlokeBotRuntimeMode.Online)
        {
            app.MapBotOAuthEndpoints();
        }
        else
        {
            app.MapUnavailableBotOAuthEndpoint();
        }

        app.MapHostConfigEndpoints();
        return app;
    }

    private static void AddAuthentication(WebApplicationBuilder builder)
    {
        builder
            .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                var webAuthOptions =
                    builder.Configuration.GetSection("TwitchWebAuth").Get<WebAuthOptions>()
                    ?? new WebAuthOptions();

                options.AccessDeniedPath = "/auth/login";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Name = string.IsNullOrWhiteSpace(webAuthOptions.CookieName)
                    ? "BlokeBot.Auth"
                    : webAuthOptions.CookieName;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.SlidingExpiration = false;
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = context =>
                        context
                            .HttpContext.RequestServices.GetRequiredService<AuthCookieValidator>()
                            .ValidateAsync(context),
                };
            });
        builder.Services.AddAuthorization(options =>
        {
            AddPolicy(options, "Operator", AuthSessionCapability.Operator);
            AddPolicy(options, "HostSelected", AuthSessionCapability.HostSelected);
            AddPolicy(options, "BotAdmin", AuthSessionCapability.BotAdmin);
        });
    }

    private static void AddPolicy(
        AuthorizationOptions options,
        string name,
        AuthSessionCapability capability
    )
    {
        options.AddPolicy(
            name,
            policy =>
                policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AuthSessionCapabilityRequirement(capability))
        );
    }
}
