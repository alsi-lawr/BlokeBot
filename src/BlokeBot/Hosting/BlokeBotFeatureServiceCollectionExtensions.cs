using BlokeBot.Auth.Moderation;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Users;
using BlokeBot.Auth.Web;
using BlokeBot.BotRuntime;
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
using BlokeBot.Features.HostedChannels;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Hosting;

public static class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAppCommands(this IServiceCollection services)
    {
        services.AddSingleton<CommandAliasRegistry>();
        services.AddSingleton<AppCommandAliasResolver>();
        return services;
    }

    public static IServiceCollection AddBlokeBotGuessing(this IServiceCollection services)
    {
        services.AddSingleton<CommandStrategyCatalog<GuessCommandKind, AppCommandRouteState>>();
        services.AddSingleton<CommandStrategyDispatcher<GuessCommandKind, AppCommandRouteState>>();
        services.AddSingleton<
            ICommandRouteResolver<GuessCommandKind, AppCommandRouteState>,
            GuessingCommandRouteResolver
        >();
        services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            StartGuessingCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            StopGuessingCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            WinGuessingCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            GuessCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            AvailableGuessesCommandStrategy
        >();
        services.AddSingleton<GuessingCommandService>();
        services.AddSingleton<GuessingConfigurationService>();
        services.AddSingleton<GuessingDashboardService>();
        services.AddSingleton<GuessingChangeNotifier>();
        services.AddSingleton<GuessingRoundService>();
        services.AddSingleton<GuessingVoteService>();
        services.AddSingleton<GuessingHistoryService>();
        services.AddSingleton<IBotHostSeeder, GuessingHostSeeder>();
        return services;
    }

    public static IServiceCollection AddBlokeBotPoints(this IServiceCollection services)
    {
        services.AddSingleton<CommandStrategyCatalog<PointsCommandKind, AppCommandRouteState>>();
        services.AddSingleton<CommandStrategyDispatcher<PointsCommandKind, AppCommandRouteState>>();
        services.AddSingleton<
            ICommandRouteResolver<PointsCommandKind, AppCommandRouteState>,
            PointsCommandRouteResolver
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            PointsBalanceCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            GivePointsCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            AddPointsCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            RemovePointsCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            GambleCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            StartGiveawayCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            JoinGiveawayCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            EndGiveawayCommandStrategy
        >();
        services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            CancelGiveawayCommandStrategy
        >();
        services.AddSingleton<PointsCommandService>();
        services.AddSingleton<PointBalanceService>();
        services.AddSingleton<PointsConfigurationService>();
        services.AddSingleton<PointsDashboardService>();
        services.AddSingleton<PointsGiveawayScheduler>();
        services.AddSingleton<IPointsGiveawayScheduler>(sp =>
            sp.GetRequiredService<PointsGiveawayScheduler>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<PointsGiveawayScheduler>());
        services.AddSingleton<PointsGiveawayDrawService>();
        services.AddSingleton<PointsGiveawayEligibilityPolicy>();
        services.AddSingleton<PointsGiveawayMessageFormatter>();
        services.AddSingleton<PointsGiveawayService>();
        services.AddSingleton<IPointsRandom, PointsRandom>();
        services.AddSingleton<PointsChangeNotifier>();
        services.AddSingleton<IBotHostSeeder, PointsHostSeeder>();
        return services;
    }

    public static IServiceCollection AddBlokeBotAdmin(this IServiceCollection services)
    {
        services.AddSingleton<BotAdminService>();
        services.AddSingleton<AdminHostManagementService>();
        services.AddSingleton<HostedChannelDirectoryService>();
        services.AddSingleton<BotAccountAuthorizationService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotSiteAccess(this IServiceCollection services)
    {
        services.AddSingleton<SiteAccessChangeNotifier>();
        services.AddScoped<SiteAccessService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotHosts(this IServiceCollection services)
    {
        services.AddSingleton<BotHostProvisioningService>();
        services.AddSingleton<BotHostRemovalService>();
        services.AddTransient<AuthorizedHostSelectionService>();
        services.AddScoped<BotHostSelectionAccessor>();
        services.AddScoped<HostConfigService>();
        services.AddSingleton<HostModAccessService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotHostedChannels(this IServiceCollection services)
    {
        services.AddSingleton<ChannelBotAuthorizationService>();
        services.AddSingleton<HostedChannelChangeNotifier>();
        services.AddSingleton<HostedChannelRuntimeControlService>();
        services.AddSingleton<HostedChannelRuntimeLifecycleService>();
        services.AddSingleton<HostedChannelRuntimeStatusService>();
        services.AddSingleton<HostFeatureService>();
        services.AddSingleton<HostBotStatusService>();
        services.AddSingleton<ITwitchBotChannelProvider, HostedChannelProvider>();
        services.AddSingleton<ITwitchBotChannelLifecycleNotifier, HostedChannelLifecycleNotifier>();
        return services;
    }

    public static IServiceCollection AddBlokeBotAuth(this IServiceCollection services)
    {
        services.AddScoped<BlokeBotPageContextAccessor>();
        services.AddSingleton<WebAuthConfiguration>();
        services.AddTransient<ModeratedChannelLookupService>();
        services.AddTransient<WebAuthService>();
        services.AddTransient<WebOAuthClient>();
        services.AddScoped<AuthSessionService>();
        services.AddSingleton<IAuthorizationHandler, AuthSessionCapabilityHandler>();
        services.AddTransient<UserLookupService>();
        services.AddTransient<ChannelBotOAuthService>();
        services.AddScoped<AuthCookieValidator>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
