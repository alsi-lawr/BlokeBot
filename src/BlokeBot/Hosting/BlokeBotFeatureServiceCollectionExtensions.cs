using BlokeBot.Auth.Moderation;
using BlokeBot.Auth.OAuth;
using BlokeBot.Auth.Sessions;
using BlokeBot.Auth.Users;
using BlokeBot.Auth.Web;
using BlokeBot.BotRuntime;
using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;
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
using BlokeBot.Features.HostedChannels;
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
using BlokeBot.Features.PublicLeaderboards;
using BlokeBot.Features.SiteAccess;
using BlokeBot.Hosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
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

    public static IServiceCollection AddBlokeBotCustomCommands(
        this IServiceCollection services,
        CustomAnnouncementDeliveryMode announcementDelivery
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<CustomCommandAliasRegistry>();
        services.AddSingleton<CustomCommandCooldownStore>();
        services.AddSingleton<CustomCommandExecutionService>();
        services.AddSingleton<CustomMessageSelector>();
        services.AddSingleton<CustomCommandTemplateRenderer>();
        services.AddSingleton<CustomCommandConfigurationGraphWriter>();
        services.AddSingleton<CustomCommandConfigurationService>();
        services.AddSingleton<HostCustomCommandSettingsService>();
        services.TryAddSingleton<ICustomAnnouncementTickScheduler, TimeProviderCustomAnnouncementTickScheduler>();
        switch (announcementDelivery)
        {
            case CustomAnnouncementDeliveryMode.Disabled:
                services.AddSingleton<ICustomAnnouncementSender, DisabledCustomAnnouncementSender>();
                break;
            case CustomAnnouncementDeliveryMode.TwitchChat:
                services.AddSingleton<ICustomAnnouncementSender, TwitchCustomAnnouncementSender>();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(announcementDelivery),
                    announcementDelivery,
                    "Unknown custom-announcement delivery mode."
                );
        }
        services.AddSingleton<ITwitchChatMessageObserver, CustomAnnouncementChatActivity>();
        services.AddSingleton<CustomAnnouncementScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<CustomAnnouncementScheduler>());
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotAlerts(this IServiceCollection services)
    {
        services.AddContinueAndReportObserverFanOut<
            OutboundQueueAlertSubscriberBoundary,
            OutboundQueueAlertNotification,
            OutboundQueueAlertSubscriberDeadLetter
        >(BlokeBotObserverBoundaries.OutboundQueueAlertSubscribers);
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddSingleton<OutboundQueueAlertSubscriberDispatcher>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IOutboundQueueAlertSubscriber,
                OutboundQueueAlertWhisperSender
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ITwitchOutboundQueueAlertObserver,
                DurableOutboundQueueAlertObserver
            >()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
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

    public static IServiceCollection AddBlokeBotPoints(
        this IServiceCollection services,
        PointsGiveawayNotificationMode notificationMode
    )
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
        services.AddSingleton<IPointTargetUserLookup, TwitchPointTargetUserLookup>();
        services.AddSingleton<PointsConfigurationService>();
        services.AddSingleton<PointsDashboardService>();
        services.AddSingleton<
            IPointsGiveawayChangeNotification,
            PointsGiveawayChangeNotification
        >();
        services.AddSingleton<
            IPointsGiveawaySchedulerOperations,
            PointsGiveawaySchedulerOperations
        >();
        services.AddSingleton(
            new PointsGiveawaySchedulerRecoveryPolicy
            {
                RetryDelay = TimeSpan.FromSeconds(30),
            }
        );
        switch (notificationMode)
        {
            case PointsGiveawayNotificationMode.ReplyOnly:
                services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    ReplyOnlyPointsGiveawaySchedulerNotification
                >();
                break;
            case PointsGiveawayNotificationMode.TwitchChat:
                services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    TwitchPointsGiveawaySchedulerNotification
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(notificationMode),
                    notificationMode,
                    "Unknown points giveaway notification mode."
                );
        }

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
        services.AddSingleton<PointsGamblingCooldownStore>();
        services.AddSingleton<PointsChangeNotifier>();
        services.AddSingleton<IBotHostSeeder, PointsHostSeeder>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotAdmin(
        this IServiceCollection services,
        BotAccountAuthorizationMode botAccountAuthorization
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
            BotAdminSettings.FromOptions(sp.GetRequiredService<IOptions<BlokeBotOptions>>().Value)
        );
        services.AddSingleton<BotAdminService>();
        services.AddSingleton<AdminHostManagementService>();
        services.AddSingleton<HostedChannelDirectoryService>();
        services.AddSingleton<BotAccountAuthorizationService>();
        switch (botAccountAuthorization)
        {
            case BotAccountAuthorizationMode.Disabled:
                services.AddSingleton<
                    IBotAccountAuthorizationPolicy,
                    DisabledBotAccountAuthorizationPolicy
                >();
                break;
            case BotAccountAuthorizationMode.Twitch:
                services.AddSingleton<BotAccountTokenStatusResolver>(serviceProvider =>
                {
                    var status = serviceProvider.GetRequiredService<TwitchTokenStatusService>();
                    return status.GetUserAccessTokenStatusAsync;
                });
                services.AddSingleton<
                    IBotAccountAuthorizationPolicy,
                    ConfiguredBotAccountAuthorizationPolicy
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(botAccountAuthorization),
                    botAccountAuthorization,
                    "Unknown bot-account authorization mode."
                );
        }
        return services;
    }

    public static IServiceCollection AddBlokeBotSiteAccess(
        this IServiceCollection services,
        AccessListProfileEnrichmentMode profileEnrichment
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<AccessListProfileResolver>();
        switch (profileEnrichment)
        {
            case AccessListProfileEnrichmentMode.Disabled:
                services.AddSingleton<
                    IAccessListProfileEnrichmentPolicy,
                    DisabledAccessListProfileEnrichmentPolicy
                >();
                break;
            case AccessListProfileEnrichmentMode.Twitch:
                services.AddSingleton<
                    IAccessListProfileEnrichmentPolicy,
                    TwitchAccessListProfileEnrichmentPolicy
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(profileEnrichment),
                    profileEnrichment,
                    "Unknown access-list profile-enrichment mode."
                );
        }
        services.AddSingleton<SiteAccessChangeNotifier>();
        services.AddScoped<SiteAccessService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotHosts(this IServiceCollection services)
    {
        services.AddSingleton<BotHostProvisioningService>();
        services.AddSingleton<BotHostRemovalService>();
        services.AddSingleton<PublicLeaderboardHostLookup>();
        services.AddTransient<AuthorizedHostSelectionService>();
        services.AddScoped<BotHostSelectionAccessor>();
        services.AddScoped<HostConfigService>();
        services.AddSingleton<HostModAccessService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotHostedChannels(
        this IServiceCollection services,
        HostBotAppAccessTokenMode appAccessToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ChannelBotAuthorizationService>();
        services.AddSingleton<HostBotAccountOAuthService>();
        services.AddSingleton<HostBotAccountAuthorizationService>();
        services.AddSingleton<IHostBotAccountTokenStatusProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBotAccountAuthorizationService>()
        );
        switch (appAccessToken)
        {
            case HostBotAppAccessTokenMode.Unavailable:
                services.AddSingleton<
                    IHostBotAppAccessTokenSource,
                    UnavailableHostBotAppAccessTokenSource
                >();
                break;
            case HostBotAppAccessTokenMode.Twitch:
                services.AddSingleton<
                    IHostBotAppAccessTokenSource,
                    TwitchHostBotAppAccessTokenSource
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(appAccessToken),
                    appAccessToken,
                    "Unknown host-bot app-access-token mode."
                );
        }
        services.AddSingleton<HostedChannelChangeNotifier>();
        services.AddSingleton<HostedChannelRuntimeControlService>();
        services.AddSingleton<HostedChannelRuntimeLifecycleService>();
        services.AddSingleton<HostedChannelRuntimeStatusService>();
        services.AddSingleton<HostFeatureService>();
        services.AddSingleton<HostBotStatusService>();
        services.AddSingleton<HostWhisperQuotaService>();
        services.AddSingleton<HostWhisperCommandResponseSender>();
        services.AddSingleton<ITwitchBotChannelProvider, HostedChannelProvider>();
        services.AddSingleton<HostedChannelLifecycleNotifier>();
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
