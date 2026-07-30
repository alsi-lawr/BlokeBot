using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.OAuth;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Auth.Users;
using BlokeBot.Core.Auth.Web;
using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.Features.AccessLists;
using BlokeBot.Core.Features.Admin.Authorization;
using BlokeBot.Core.Features.Admin.HostedChannels;
using BlokeBot.Core.Features.Alerts;
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
using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Points.HostSetup;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Features.SiteAccess;
using BlokeBot.Core.Hosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Hosting;

public static class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAppCommands(this IServiceCollection services)
    {
        services.AddSingleton<CommandAliasRegistry>();
        services.AddSingleton<AppCommandAliasResolver>();
        return services;
    }

    public static IServiceCollection AddBlokeBotRequestBoards(this IServiceCollection services)
    {
        services.AddSingleton<RequestBoardService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
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
        services.AddSingleton<CustomCommandInvocationClaimStore>();
        services.AddSingleton<CustomCommandInvocationResetService>();
        services.TryAddSingleton<
            ICustomCommandViewerResolver,
            UnavailableCustomCommandViewerResolver
        >();
        services.TryAddSingleton<
            IHostStreamLivenessProvider,
            UnavailableCustomCommandStreamLivenessProvider
        >();
        services.AddSingleton<CustomMessageSelector>();
        services.AddSingleton<CustomCommandTemplateRenderer>();
        services.AddSingleton<CustomCommandConfigurationGraphWriter>();
        services.AddSingleton<CustomCommandConfigurationService>();
        services.AddSingleton<HostCustomCommandSettingsService>();
        services.AddSingleton<TwitchAnnouncementAccessService>();
        services.AddSingleton<ITwitchAnnouncementAccessService>(serviceProvider =>
            serviceProvider.GetRequiredService<TwitchAnnouncementAccessService>()
        );
        services.AddSingleton<ITwitchAnnouncementReadinessProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TwitchAnnouncementAccessService>()
        );
        services.TryAddSingleton<
            ICustomAnnouncementTickScheduler,
            TimeProviderCustomAnnouncementTickScheduler
        >();
        switch (announcementDelivery)
        {
            case CustomAnnouncementDeliveryMode.Disabled:
                services.AddSingleton<
                    ICustomAnnouncementSender,
                    DisabledCustomAnnouncementSender
                >();
                break;
            case CustomAnnouncementDeliveryMode.PublicChat:
                services.AddSingleton<
                    ICustomAnnouncementSender,
                    TwitchAnnouncementCustomAnnouncementSender
                >();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(announcementDelivery),
                    announcementDelivery,
                    "Unknown custom-announcement delivery mode."
                );
        }
        services.AddSingleton<IChatMessageObserver, CustomAnnouncementChatActivity>();
        services.AddSingleton<CustomAnnouncementScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<CustomAnnouncementScheduler>());
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotAlerts(this IServiceCollection services)
    {
        services.TryAddSingleton<DurableAlertService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPublicChatQueueAlertObserver,
                DurablePublicChatQueueAlertObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPublicChatTerminalRejectionObserver,
                DurableFollowerOnlyChatAlertObserver
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
        services.AddSingleton<IPointTargetUserLookup, HelixPointTargetUserLookup>();
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
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.FromSeconds(30) }
        );
        switch (notificationMode)
        {
            case PointsGiveawayNotificationMode.ReplyOnly:
                services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    ReplyOnlyPointsGiveawaySchedulerNotification
                >();
                break;
            case PointsGiveawayNotificationMode.PublicChat:
                services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    PublicChatPointsGiveawaySchedulerNotification
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
                services.AddSingleton<ITokenStatusSource, UnavailableTokenStatusSource>();
                services.AddSingleton<
                    IBotAccountAuthorizationPolicy,
                    DisabledBotAccountAuthorizationPolicy
                >();
                break;
            case BotAccountAuthorizationMode.Twitch:
                services.AddSingleton<ITokenStatusSource, TokenStatusService>();
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
                    HelixAccessListProfileEnrichmentPolicy
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
        services.AddSingleton<HostBotOAuthStateStore>();
        services.AddSingleton<HostBroadcasterOAuthStateStore>();
        services.AddDataProtection();
        services.TryAddSingleton<
            IHostBotAccountTokenProtector,
            DataProtectionHostBotAccountTokenProtector
        >();
        services.AddSingleton<HostBotAccountAuthorizationService>();
        services.AddSingleton<HostBroadcasterAuthorizationService>();
        services.AddSingleton<IHostBroadcasterTokenStatusProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBroadcasterAuthorizationService>()
        );
        services.AddSingleton<IBroadcasterAccountProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBroadcasterAuthorizationService>()
        );
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
                    OAuthHostBotAppAccessTokenSource
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
        services.AddSingleton<IHostStreamLivenessProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBotStatusService>()
        );
        services.AddSingleton<ICustomCommandViewerResolver, CustomCommandViewerResolver>();
        services.AddSingleton<FollowerOnlyChatReadinessService>();
        services.AddSingleton<WhisperQuotaService>();
        services.AddSingleton<StartupMessageConfigurationService>();
        services.AddSingleton<IStartupChatMessageProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<StartupMessageConfigurationService>()
        );
        services.AddSingleton<
            IPrivateDeliveryFailureHandler,
            PrivateDeliveryFailureTelemetryHandler
        >();
        services.AddSingleton<WhisperCommandResponseSender>();
        services.AddSingleton<IBotChannelProvider, HostedChannelProvider>();
        services.AddSingleton<HostedChannelLifecycleNotifier>();
        return services;
    }

    public static IServiceCollection AddBlokeBotAuth(this IServiceCollection services)
    {
        services.AddScoped<BlokeBotPageContextAccessor>();
        services.AddSingleton<WebAuthConfiguration>();
        services.AddTransient<ModeratedChannelLookupService>();
        services.AddSingleton<ModeratorAuthorityService>();
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
