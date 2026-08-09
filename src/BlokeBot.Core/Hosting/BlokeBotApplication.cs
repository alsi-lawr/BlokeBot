using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.BotStatus;
using BlokeBot.Core.Components;
using BlokeBot.Core.Components.Layout;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        _ = builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        _ = builder.Services.AddCascadingAuthenticationState();
        _ = builder.Services.AddHttpContextAccessor();
        _ = builder.Services.AddSingleton(BlokeBotBuildIdentity.Current);
        _ = builder.Services.AddSingleton<UiFaultTelemetry>();
        _ = builder.Services.AddScoped<DashboardFragmentState>();

        _ = builder
            .Services.AddOptions<BlokeBotOptions>()
            .BindConfiguration("BlokeBot")
            .Validate(BlokeBotOptionsValidation.IsValid, "BlokeBot options are invalid.")
            .ValidateOnStart();
        _ = builder
            .Services.AddOptions<WebAuthOptions>()
            .BindConfiguration("TwitchWebAuth")
            .ValidateOnStart();
        var privacy = builder
            .Services.AddOptions<PrivacyNoticeOptions>()
            .BindConfiguration("BlokeBotPrivacy")
            .Validate(
                PrivacyNoticeOptionsValidation.HasValidNoticeUrlWhenConfigured,
                PrivacyNoticeOptionsValidation.NoticeUrlFailure
            )
            .ValidateOnStart();
        if (
            PrivacyNoticeOptionsValidation.RequiredFor(
                runtime == BlokeBotRuntimeMode.Online,
                builder.Environment.EnvironmentName
            )
        )
        {
            _ = privacy.Validate(
                PrivacyNoticeOptionsValidation.IsComplete,
                PrivacyNoticeOptionsValidation.RequiredFailure
            );
        }
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        var twitchEndpoints =
            builder
                .Configuration.GetSection(TwitchEndpointPolicy.ConfigurationSectionName)
                .Get<TwitchEndpointPolicy>()
            ?? new TwitchEndpointPolicy();
        twitchEndpoints.Validate();
        _ = builder.Services.AddSingleton(twitchEndpoints);

        var online = runtime == BlokeBotRuntimeMode.Online;
        _ = builder.Services.AddEventBus<AppEventKind>(
            ObserverBoundary.Named("BlokeBot.ApplicationEvents"),
            static eventKind => ObserverEventIdentity.Named($"BlokeBot.{eventKind}")
        );
        _ = builder
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
            .AddBlokeBotAutomations()
            .AddBlokeBotHosts()
            .AddBlokeBotGuessing()
            .AddBlokeBotPoints(
                online
                    ? PointsGiveawayNotificationMode.PublicChat
                    : PointsGiveawayNotificationMode.ReplyOnly
            )
            .AddBlokeBotRequestBoards()
            .AddBlokeBotPlayWithViewers()
            .AddBlokeBotMoments()
            .AddBlokeBotOverlays()
            .AddBlokeBotToasts()
            .AddBlokeBotTwitchOperations()
            .AddBlokeBotAuth();
        _ = builder.Services.AddOAuthTransport();
        _ = builder.Services.AddHelix();
        _ = builder.Services.AddHttpClient();
        AddAuthentication(builder);

        var botSection = builder.Configuration.GetSection("TwitchBot");
        if (online)
        {
            _ = builder
                .Services.AddTwitchBot(
                    botSection,
                    online: online && !builder.Environment.IsEnvironment("Simulation")
                )
                .UseBlokeBotHostedChannelProvider()
                .UseWhisperCommandResponseSender()
                .UseBlokeBotHostedChannelLifecycleNotifier()
                .AddCommandModule<ViewerCommandCatalogModule>()
                .AddCommandModule<CommandStrategyModule<GuessCommandKind, AppCommandRouteState>>()
                .AddCommandModule<CommandStrategyModule<PointsCommandKind, AppCommandRouteState>>()
                .AddCommandModule<RequestBoardCommandModule>()
                .AddCommandModule<PlayQueueCommandModule>()
                .AddCommandModule<MomentCommandModule>()
                .AddCommandModule<CustomCommandModule>();
        }
        else
        {
            _ = builder.Services.AddTwitchBotSettings(botSection);
            _ = builder.Services.AddUnavailableAccessTokenProvider();
            _ = builder.Services.AddOfflineBotRuntimeStatus();
            _ = builder.Services.Replace(
                ServiceDescriptor.Singleton<IPointTargetUserLookup, OfflinePointTargetUserLookup>()
            );
            _ = builder.Services.Replace(
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
        await app
            .Services.GetRequiredService<HostedChannelRuntimeLifecycleService>()
            .RecoverInterruptedStopsAsync(cancellationToken);
    }

    public static WebApplication UseBlokeBotCore(
        this WebApplication app,
        BlokeBotRuntimeMode runtime
    )
    {
        app.UseOverlayAccessLogRedaction();

        if (!app.Environment.IsDevelopment())
        {
            _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
            _ = app.UseHsts();
        }

        _ = app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        _ = app.UseHttpsRedirection();
        _ = app.UseAntiforgery();
        _ = app.UseAuthentication();
        _ = app.UseAuthorization();

        app.MapOverlayBrowserSourceEndpoints();
        _ = app.MapMethods(
            "/favicon.ico",
            ["GET", "HEAD"],
            static () => Results.Redirect("/blokebot-mark.svg")
        );
        _ = app.UseStaticFiles();
        _ = app.MapStaticAssets();
        _ = app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();
        app.MapAuthEndpoints();
        if (runtime == BlokeBotRuntimeMode.Online)
        {
            app.MapEventSubWebhookEndpoint();
        }
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

    internal static void MapEventSubWebhookEndpoint(this WebApplication app) =>
        _ = app.MapPost(
                "/eventsub/twitch",
                async (
                    HttpRequest request,
                    IEventSubWebhookIngress ingress,
                    CancellationToken ct
                ) =>
                {
                    const int MaxBodyBytes = 512 * 1024;
                    if (request.ContentLength is > MaxBodyBytes)
                    {
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }

                    await using var stream = new MemoryStream();
                    var buffer = new byte[16 * 1024];
                    var total = 0;
                    while (true)
                    {
                        var read = await request.Body.ReadAsync(buffer, ct);
                        if (read is 0)
                        {
                            break;
                        }

                        total += read;
                        if (total > MaxBodyBytes)
                        {
                            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                        }

                        await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                    }

                    var result = await ingress.HandleAsync(
                        request.Headers["Twitch-Eventsub-Message-Id"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Message-Type"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Message-Timestamp"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Message-Signature"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Subscription-Type"].FirstOrDefault(),
                        request.Headers["Twitch-Eventsub-Subscription-Version"].FirstOrDefault(),
                        stream.ToArray(),
                        ct
                    );
                    return result.Challenge is null
                        ? Results.StatusCode(result.StatusCode)
                        : Results.Text(result.Challenge, "text/plain");
                }
            )
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithMetadata(new SkipStatusCodePagesAttribute());

    private static void AddAuthentication(WebApplicationBuilder builder)
    {
        _ = builder
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
        _ = builder.Services.AddAuthorization(options =>
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
    ) =>
        options.AddPolicy(
            name,
            policy =>
                policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new AuthSessionCapabilityRequirement(capability))
        );
}
