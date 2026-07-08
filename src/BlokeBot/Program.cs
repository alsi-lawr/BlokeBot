using Alsi.TwitchBot;
using BlokeBot;
using BlokeBot.Eventing;
using BlokeBot.Auth.Hosts;
using BlokeBot.Auth.Moderation;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Users;
using BlokeBot.Auth.Web;
using BlokeBot.BotRuntime;
using BlokeBot.BotStatus;
using BlokeBot.Components;
using BlokeBot.Features.Admin.Authorization;
using BlokeBot.Features.Admin.HostedChannels;
using BlokeBot.Features.Commands;
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
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Points.HostSetup;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddOptions<BlokeBotOptions>().BindConfiguration("BlokeBot").ValidateOnStart();
builder.Services.AddOptions<WebAuthOptions>().BindConfiguration("TwitchWebAuth").ValidateOnStart();
builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddBlokeBotPersistence(
    builder.Configuration.GetSection("BlokeBot").Get<BlokeBotOptions>()?.DatabasePath
        ?? new BlokeBotOptions().DatabasePath
);
builder.Services.AddSingleton<CommandAliasRegistry>();
builder.Services.AddSingleton<AppCommandCatalog>();
builder.Services.AddSingleton<AppCommandDispatcher>();
builder.Services.AddSingleton<GuessingCommandService>();
builder.Services.AddSingleton<GuessingConfigurationService>();
builder.Services.AddSingleton<GuessingDashboardService>();
builder.Services.AddSingleton<GuessingChangeNotifier>();
builder.Services.AddSingleton<GuessingRoundService>();
builder.Services.AddSingleton<GuessingVoteService>();
builder.Services.AddSingleton<GuessingHistoryService>();
builder.Services.AddSingleton<IBotHostSeeder, GuessingHostSeeder>();
builder.Services.AddSingleton<GuessingCommandModule>();
builder.Services.AddSingleton<PointsCommandService>();
builder.Services.AddSingleton<PointBalanceService>();
builder.Services.AddSingleton<PointsConfigurationService>();
builder.Services.AddSingleton<PointsDashboardService>();
builder.Services.AddSingleton<PointsGiveawayScheduler>();
builder.Services.AddSingleton<IPointsGiveawayScheduler>(sp =>
    sp.GetRequiredService<PointsGiveawayScheduler>()
);
builder.Services.AddHostedService(sp => sp.GetRequiredService<PointsGiveawayScheduler>());
builder.Services.AddSingleton<PointsGiveawayService>();
builder.Services.AddSingleton<IPointsRandom, PointsRandom>();
builder.Services.AddSingleton<PointsChangeNotifier>();
builder.Services.AddSingleton<PointsCommandModule>();
builder.Services.AddSingleton<IBotHostSeeder, PointsHostSeeder>();
builder.Services.AddSingleton<BotAdminService>();
builder.Services.AddSingleton<SiteAccessChangeNotifier>();
builder.Services.AddScoped<SiteAccessService>();
builder.Services.AddSingleton<BotHostProvisioningService>();
builder.Services.AddSingleton<BotHostRemovalService>();
builder.Services.AddSingleton<EventBus<AppEventKind>>();
builder.Services.TryAddSingleton<TwitchOAuthApiClient>();
builder.Services.TryAddSingleton<TwitchHelixApiClient>();
builder.Services.AddSingleton<ChannelBotAuthorizationService>();
builder.Services.AddSingleton<BotAccountAuthorizationService>();
builder.Services.AddSingleton<HostedChannelChangeNotifier>();
builder.Services.AddSingleton<HostedChannelDirectoryService>();
builder.Services.AddSingleton<HostedChannelRuntimeControlService>();
builder.Services.AddSingleton<HostedChannelRuntimeLifecycleService>();
builder.Services.AddSingleton<HostedChannelRuntimeStatusService>();
builder.Services.AddSingleton<HostBotStatusService>();
builder.Services.AddScoped<HostConfigService>();
builder.Services.AddSingleton<HostModAccessService>();
builder.Services.AddSingleton<ITwitchBotChannelProvider, HostedChannelProvider>();
builder.Services.AddSingleton<ITwitchBotChannelLifecycleNotifier, HostedChannelLifecycleNotifier>();
builder.Services.AddScoped<BotHostSelectionAccessor>();
builder.Services.AddScoped<BlokeBotPageContextAccessor>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<WebAuthConfiguration>();
builder.Services.AddTransient<AuthorizedHostResolver>();
builder.Services.AddTransient<ModeratedChannelLookupService>();
builder.Services.AddTransient<WebAuthService>();
builder.Services.AddTransient<WebOAuthClient>();
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddSingleton<IAuthorizationHandler, AuthSessionCapabilityHandler>();
builder.Services.AddTransient<UserLookupService>();
builder.Services.AddTransient<ChannelBotOAuthService>();
builder.Services.AddScoped<AuthCookieValidator>();
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
                .AddRequirements(new AuthSessionCapabilityRequirement(AuthSessionCapability.Operator))
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
                .AddRequirements(new AuthSessionCapabilityRequirement(AuthSessionCapability.BotAdmin))
    );
});

var botSection = builder.Configuration.GetSection("TwitchBot");
builder.Services.AddOptions<TwitchBotOptions>().Bind(botSection);
var botRuntimeConfigured = IsBotRuntimeConfigured(botSection);
if (botRuntimeConfigured)
{
    builder.Services.AddTwitchBot(botSection).AddCommandModule<AppCommandRouterModule>();
}
builder.Services.TryAddSingleton<ITwitchBotRuntimeStatusAccessor, OfflineBotStatusAccessor>();

var app = builder.Build();

await app
    .Services.GetRequiredService<BlokeBotDatabaseMigrator>()
    .ApplyMigrationsAsync(CancellationToken.None);

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
app.MapBotOAuthEndpoints(botRuntimeConfigured);
app.MapHostConfigEndpoints();

app.Run();

static bool IsBotRuntimeConfigured(IConfiguration section)
{
    var identity = section.GetSection("Identity");
    return !string.IsNullOrWhiteSpace(identity["BotUsername"])
        && !string.IsNullOrWhiteSpace(identity["ClientId"])
        && !string.IsNullOrWhiteSpace(identity["ClientSecret"])
        && !string.IsNullOrWhiteSpace(identity["RedirectUri"]);
}
