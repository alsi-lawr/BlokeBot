using BlokeBot;
using BlokeBot.Auth.Moderation;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Users;
using BlokeBot.Auth.Web;
using BlokeBot.BotRuntime;
using BlokeBot.BotStatus;
using BlokeBot.Cli;
using BlokeBot.Components;
using BlokeBot.Eventing;
using BlokeBot.Features.AccessLists;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Commands;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Features.Guessing.Commands;
using BlokeBot.Features.Guessing.Configuration;
using BlokeBot.Features.Guessing.Game;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.History;
using BlokeBot.Features.Guessing.HostSetup;
using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Features.HostConfig.Access;
using BlokeBot.Features.HostConfig.Page;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.HostedChannels.Whispers;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Points.HostSetup;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Features.Toasts;
using BlokeBot.Hosting;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Simulation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;

var cliInvocation = BlokeBotCli.Parse(args);
if (cliInvocation is not BlokeBotCliInvocation.Serve serve)
{
    var response = BlokeBotCli.Render(cliInvocation);
    Console.Out.Write(response.StandardOutput);
    Console.Error.Write(response.StandardError);
    return response.ExitCode;
}

var builder = WebApplication.CreateBuilder(serve.AspNetArguments.ToArray());
if (!BlokeBotServerUrlPolicy.HasExplicitConfiguration(builder.Configuration))
{
    builder.WebHost.UseUrls(BlokeBotServerUrlPolicy.DefaultUrl);
}

var (operatingSystem, platformEnvironment) = BlokeBotPlatformEnvironment.Current();
var pathResolution = BlokeBotStatePathResolver.Resolve(
    new BlokeBotStatePathRequest(
        operatingSystem,
        platformEnvironment,
        serve.DataDirectory,
        builder.Configuration["BlokeBot:DatabasePath"],
        builder.Configuration["TwitchBot:Identity:TokenCachePath"]
    )
);
if (pathResolution is BlokeBotStatePathResolution.Failed pathFailure)
{
    Console.Error.WriteLine($"blokebot: {pathFailure.Message}");
    return 1;
}

var resolvedPaths = ((BlokeBotStatePathResolution.Resolved)pathResolution).Paths;
var pathPreparation = BlokeBotStatePathPreparer.Prepare(resolvedPaths);
if (pathPreparation is BlokeBotStatePathPreparation.Failed preparationFailure)
{
    Console.Error.WriteLine(preparationFailure.Message);
    return 1;
}

var statePaths = ((BlokeBotStatePathPreparation.Prepared)pathPreparation).Paths;
builder.Configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["BlokeBot:DatabasePath"] = statePaths.DatabasePath,
        ["TwitchBot:Identity:TokenCachePath"] = statePaths.TokenCachePath,
    }
);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<UiFaultTelemetry>();

builder
    .Services.AddOptions<BlokeBotOptions>()
    .BindConfiguration("BlokeBot")
    .Validate(BlokeBotOptionsValidation.IsValid, "BlokeBot options are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<WebAuthOptions>().BindConfiguration("TwitchWebAuth").ValidateOnStart();
builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);

var botSection = builder.Configuration.GetSection("TwitchBot");
var missingTwitchFields = BlokeBotTwitchConfiguration.MissingFields(builder.Configuration);
var botRuntimeConfigured = missingTwitchFields.Count == 0;

builder.Services.AddBlokeBotPersistence(
    builder.Configuration.GetSection("BlokeBot").Get<BlokeBotOptions>()?.DatabasePath
        ?? new BlokeBotOptions().DatabasePath
);
builder.Services.AddEventBus<AppEventKind>(
    ObserverBoundary.Named("BlokeBot.ApplicationEvents"),
    eventKind => ObserverEventIdentity.Named($"BlokeBot.{eventKind}")
);
builder
    .Services.AddBlokeBotAppCommands()
    .AddBlokeBotPublicChat()
    .AddBlokeBotAlerts()
    .AddBlokeBotCustomCommands(
        botRuntimeConfigured
            ? CustomAnnouncementDeliveryMode.PublicChat
            : CustomAnnouncementDeliveryMode.Disabled
    )
    .AddBlokeBotSiteAccess(
        botRuntimeConfigured
            ? AccessListProfileEnrichmentMode.Twitch
            : AccessListProfileEnrichmentMode.Disabled
    )
    .AddBlokeBotAdmin(
        botRuntimeConfigured
            ? BotAccountAuthorizationMode.Twitch
            : BotAccountAuthorizationMode.Disabled
    )
    .AddBlokeBotHostedChannels(
        botRuntimeConfigured
            ? HostBotAppAccessTokenMode.Twitch
            : HostBotAppAccessTokenMode.Unavailable
    )
    .AddBlokeBotHosts()
    .AddBlokeBotGuessing()
    .AddBlokeBotPoints(
        botRuntimeConfigured
            ? PointsGiveawayNotificationMode.PublicChat
            : PointsGiveawayNotificationMode.ReplyOnly
    )
    .AddBlokeBotToasts()
    .AddBlokeBotAuth();
builder.Services.AddOAuthTransport();
builder.Services.AddHelix();
builder.Services.AddHttpClient();
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
    options.AddPolicy(
        "Operator",
        policy =>
            policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new AuthSessionCapabilityRequirement(AuthSessionCapability.Operator)
                )
    );
    options.AddPolicy(
        "HostSelected",
        policy =>
            policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new AuthSessionCapabilityRequirement(AuthSessionCapability.HostSelected)
                )
    );
    options.AddPolicy(
        "BotAdmin",
        policy =>
            policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new AuthSessionCapabilityRequirement(AuthSessionCapability.BotAdmin)
                )
    );
});

if (botRuntimeConfigured)
{
    builder
        .Services.AddTwitchBot(botSection)
        .UseBlokeBotHostedChannelProvider()
        .UseWhisperCommandResponseSender()
        .UseBlokeBotHostedChannelLifecycleNotifier()
        .AddCommandModule<CommandStrategyModule<GuessCommandKind, AppCommandRouteState>>()
        .AddCommandModule<CommandStrategyModule<PointsCommandKind, AppCommandRouteState>>()
        .AddCommandModule<CustomCommandModule>();
}
else
{
    builder.Services.AddTwitchBotSettings(botSection);
    builder.Services.AddUnavailableAccessTokenProvider();
    builder.Services.AddOfflineBotRuntimeStatus();
}

var simulationEnabled = builder.Environment.IsEnvironment(SimulationMode.EnvironmentName);
if (simulationEnabled)
{
    builder.WebHost.UseStaticWebAssets();
    builder.Services.AddBlokeBotSimulation();
}

await using var app = builder.Build();

await app
    .Services.GetRequiredService<BlokeBotDatabaseInitializer>()
    .InitializeAsync(CancellationToken.None);

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

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();

app.MapAuthEndpoints();
if (simulationEnabled)
{
    app.MapSimulationEndpoints();
}
if (botRuntimeConfigured)
{
    app.MapBotOAuthEndpoints();
}
else
{
    app.MapUnavailableBotOAuthEndpoint();
}
app.MapHostConfigEndpoints();

await app.StartAsync();
if (missingTwitchFields.Count > 0)
{
    Console.Out.WriteLine(BlokeBotTwitchConfiguration.OfflineGuidance(missingTwitchFields));
}

var server = app.Services.GetRequiredService<IServer>();
var localUrl = BlokeBotServerUrlPolicy.LocalUrl(server.Features.Get<IServerAddressesFeature>());
Console.Out.WriteLine($"BlokeBot is available at {localUrl}");
await app.WaitForShutdownAsync();
return 0;
