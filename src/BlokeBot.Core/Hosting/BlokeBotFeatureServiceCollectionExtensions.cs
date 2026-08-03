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
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PlayWithViewers;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Hosting;

public static class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAppCommands(this IServiceCollection services)
    {
        _ = services.AddSingleton<CommandAliasRegistry>();
        _ = services.AddSingleton<AppCommandAliasResolver>();
        _ = services.AddSingleton<CommandsConfigurationService>();
        _ = services.AddSingleton<ViewerCommandCatalogService>();
        _ = services.AddSingleton<IBotHostSeeder, CommandsHostSeeder>();
        return services;
    }

    public static IServiceCollection AddBlokeBotRequestBoards(this IServiceCollection services)
    {
        _ = services.AddSingleton<RequestBoardService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotPlayWithViewers(this IServiceCollection services)
    {
        _ = services.AddSingleton<PlayQueueChangeNotifier>();
        _ = services.AddSingleton<PlayQueueService>();
        _ = services.AddSingleton<IPlayQueueProjectionReader>(serviceProvider =>
            serviceProvider.GetRequiredService<PlayQueueService>()
        );
        _ = services.AddSingleton<IPrivateLobbyDelivery, TwitchPrivateLobbyDelivery>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotMoments(this IServiceCollection services)
    {
        _ = services.AddSingleton<MomentHubService>();
        _ = services.AddSingleton<IMomentProviderOperations, MomentProviderOperations>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotOverlays(this IServiceCollection services)
    {
        _ = services.AddSingleton<
            IOverlayAccessKeyGenerator,
            CryptographicOverlayAccessKeyGenerator
        >();
        _ = services.AddSingleton<OverlayInstanceService>();
        _ = services.AddSingleton<OverlayInstanceResolver>();
        _ = services.AddSingleton<OverlayManagementAuthority>();
        _ = services.AddSingleton<IOverlayDnsResolver, SystemOverlayDnsResolver>();
        _ = services.AddSingleton<OverlayRemoteUrlPolicy>();
        _ = services.AddSingleton<IOverlayMediaFileDeletion, SystemOverlayMediaFileDeletion>();
        _ = services.AddSingleton<OverlayCueService>();
        _ = services.AddSingleton<OverlayServerEpoch>();
        _ = services.AddSingleton<OverlayEventFeedService>();
        _ = services.AddSingleton<IOverlayEventPresenter>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayEventFeedService>()
        );
        _ = services.AddSingleton<IHostFeatureChangeObserver>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayEventFeedService>()
        );
        _ = services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayEventFeedService>()
        );
        _ = services.AddSingleton<IOverlayStateProvider, OverlayStateProvider>();
        _ = services.AddSingleton<OverlayLiveCoordinator>();
        _ = services.AddSingleton<IOverlayLivePublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IOverlayLivePresence>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IOverlayCueTransport>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<OverlayCuePlaybackService>();
        _ = services.AddSingleton<IOverlayCueAdmissionService>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayCuePlaybackService>()
        );
        _ = services.AddSingleton<IGuessingChangeObserver>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IPointsGiveawayChangeObserver>(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<OverlayCuePlaybackService>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotCustomCommands(
        this IServiceCollection services,
        CustomAnnouncementDeliveryMode announcementDelivery
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<CustomCommandAliasRegistry>();
        _ = services.AddSingleton<CustomCommandCooldownStore>();
        _ = services.AddSingleton<CustomCommandExecutionService>();
        services.TryAddSingleton<
            IOverlayCueAdmissionService,
            UnavailableOverlayCueAdmissionService
        >();
        _ = services.AddSingleton<CustomCommandInvocationClaimStore>();
        _ = services.AddSingleton<CustomCommandInvocationResetService>();
        services.TryAddSingleton<
            ICustomCommandViewerResolver,
            UnavailableCustomCommandViewerResolver
        >();
        services.TryAddSingleton<
            IHostStreamLivenessProvider,
            UnavailableCustomCommandStreamLivenessProvider
        >();
        _ = services.AddSingleton<CustomMessageSelector>();
        _ = services.AddSingleton<CustomCommandTemplateRenderer>();
        _ = services.AddSingleton<CustomCommandConfigurationGraphWriter>();
        _ = services.AddSingleton<CustomCommandConfigurationService>();
        _ = services.AddSingleton<HostCustomCommandSettingsService>();
        _ = services.AddSingleton<TwitchAnnouncementAccessService>();
        _ = services.AddSingleton<ITwitchAnnouncementAccessService>(serviceProvider =>
            serviceProvider.GetRequiredService<TwitchAnnouncementAccessService>()
        );
        _ = services.AddSingleton<ITwitchAnnouncementReadinessProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<TwitchAnnouncementAccessService>()
        );
        services.TryAddSingleton<
            ICustomAnnouncementTickScheduler,
            TimeProviderCustomAnnouncementTickScheduler
        >();
        switch (announcementDelivery)
        {
            case CustomAnnouncementDeliveryMode.Disabled:
                _ = services.AddSingleton<
                    ICustomAnnouncementSender,
                    DisabledCustomAnnouncementSender
                >();
                break;
            case CustomAnnouncementDeliveryMode.PublicChat:
                _ = services.AddSingleton<
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
        _ = services.AddSingleton<IChatMessageObserver, CustomAnnouncementChatActivity>();
        _ = services.AddSingleton<CustomAnnouncementScheduler>();
        _ = services.AddHostedService(sp => sp.GetRequiredService<CustomAnnouncementScheduler>());
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
        _ = services.AddSingleton<CommandStrategyCatalog<GuessCommandKind, AppCommandRouteState>>();
        _ = services.AddSingleton<
            CommandStrategyDispatcher<GuessCommandKind, AppCommandRouteState>
        >();
        _ = services.AddSingleton<
            ICommandRouteResolver<GuessCommandKind, AppCommandRouteState>,
            GuessingCommandRouteResolver
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            StartGuessingCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            StopGuessingCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            WinGuessingCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            GuessCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<GuessCommandKind, AppCommandRouteState>,
            AvailableGuessesCommandStrategy
        >();
        _ = services.AddSingleton<GuessingCommandService>();
        _ = services.AddSingleton<GuessingConfigurationService>();
        _ = services.AddSingleton<GuessingDashboardService>();
        _ = services.AddSingleton<GuessingChangeNotifier>();
        _ = services.AddSingleton<GuessingRoundService>();
        _ = services.AddSingleton<GuessingVoteService>();
        _ = services.AddSingleton<GuessingHistoryService>();
        _ = services.AddSingleton<IBotHostSeeder, GuessingHostSeeder>();
        return services;
    }

    public static IServiceCollection AddBlokeBotPoints(
        this IServiceCollection services,
        PointsGiveawayNotificationMode notificationMode
    )
    {
        _ = services.AddSingleton<
            CommandStrategyCatalog<PointsCommandKind, AppCommandRouteState>
        >();
        _ = services.AddSingleton<
            CommandStrategyDispatcher<PointsCommandKind, AppCommandRouteState>
        >();
        _ = services.AddSingleton<
            ICommandRouteResolver<PointsCommandKind, AppCommandRouteState>,
            PointsCommandRouteResolver
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            PointsBalanceCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            GivePointsCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            AddPointsCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            RemovePointsCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            GambleCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            StartGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            JoinGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            EndGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<
            ICommandStrategy<PointsCommandKind, AppCommandRouteState>,
            CancelGiveawayCommandStrategy
        >();
        _ = services.AddSingleton<PointsCommandService>();
        _ = services.AddSingleton<PointBalanceService>();
        _ = services.AddSingleton<IPointTargetUserLookup, HelixPointTargetUserLookup>();
        _ = services.AddSingleton<PointsConfigurationService>();
        _ = services.AddSingleton<PointsDashboardService>();
        _ = services.AddSingleton<PointsGiveawayChangeNotifier>();
        _ = services.AddSingleton<
            IPointsGiveawayChangeNotification,
            PointsGiveawayChangeNotification
        >();
        _ = services.AddSingleton<
            IPointsGiveawaySchedulerOperations,
            PointsGiveawaySchedulerOperations
        >();
        _ = services.AddSingleton(
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.FromSeconds(30) }
        );
        switch (notificationMode)
        {
            case PointsGiveawayNotificationMode.ReplyOnly:
                _ = services.AddSingleton<
                    IPointsGiveawaySchedulerNotification,
                    ReplyOnlyPointsGiveawaySchedulerNotification
                >();
                break;
            case PointsGiveawayNotificationMode.PublicChat:
                _ = services.AddSingleton<
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

        _ = services.AddSingleton<PointsGiveawayScheduler>();
        _ = services.AddSingleton<IPointsGiveawayScheduler>(sp =>
            sp.GetRequiredService<PointsGiveawayScheduler>()
        );
        _ = services.AddHostedService(sp => sp.GetRequiredService<PointsGiveawayScheduler>());
        _ = services.AddSingleton<PointsGiveawayDrawService>();
        _ = services.AddSingleton<PointsGiveawayEligibilityPolicy>();
        _ = services.AddSingleton<PointsGiveawayMessageFormatter>();
        _ = services.AddSingleton<PointsGiveawayService>();
        _ = services.AddSingleton<IPointsRandom, PointsRandom>();
        _ = services.AddSingleton<PointsGamblingCooldownStore>();
        _ = services.AddSingleton<PointsChangeNotifier>();
        _ = services.AddSingleton<IBotHostSeeder, PointsHostSeeder>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotAdmin(
        this IServiceCollection services,
        BotAccountAuthorizationMode botAccountAuthorization
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton(sp =>
            BotAdminSettings.FromOptions(sp.GetRequiredService<IOptions<BlokeBotOptions>>().Value)
        );
        _ = services.AddSingleton<BotAdminService>();
        _ = services.AddSingleton<AdminHostManagementService>();
        _ = services.AddSingleton<HostedChannelDirectoryService>();
        _ = services.AddSingleton<BotAccountAuthorizationService>();
        switch (botAccountAuthorization)
        {
            case BotAccountAuthorizationMode.Disabled:
                _ = services.AddSingleton<ITokenStatusSource, UnavailableTokenStatusSource>();
                _ = services.AddSingleton<
                    IBotAccountAuthorizationPolicy,
                    DisabledBotAccountAuthorizationPolicy
                >();
                break;
            case BotAccountAuthorizationMode.Twitch:
                _ = services.AddSingleton<ITokenStatusSource, TokenStatusService>();
                _ = services.AddSingleton<
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

        _ = services.AddTransient<AccessListProfileResolver>();
        switch (profileEnrichment)
        {
            case AccessListProfileEnrichmentMode.Disabled:
                _ = services.AddSingleton<
                    IAccessListProfileEnrichmentPolicy,
                    DisabledAccessListProfileEnrichmentPolicy
                >();
                break;
            case AccessListProfileEnrichmentMode.Twitch:
                _ = services.AddSingleton<
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
        _ = services.AddSingleton<SiteAccessChangeNotifier>();
        _ = services.AddScoped<SiteAccessService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotHosts(this IServiceCollection services)
    {
        _ = services.AddSingleton<BotHostProvisioningService>();
        _ = services.AddSingleton<BotHostRemovalService>();
        _ = services.AddSingleton<PublicLeaderboardHostLookup>();
        _ = services.AddTransient<AuthorizedHostSelectionService>();
        _ = services.AddScoped<BotHostSelectionAccessor>();
        _ = services.AddScoped<HostConfigService>();
        _ = services.AddSingleton<HostModAccessService>();
        return services;
    }

    public static IServiceCollection AddBlokeBotHostedChannels(
        this IServiceCollection services,
        HostBotAppAccessTokenMode appAccessToken
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services.AddSingleton<ChannelBotAuthorizationService>();
        _ = services.AddSingleton<HostBotAccountOAuthService>();
        _ = services.AddSingleton<HostBotOAuthStateStore>();
        _ = services.AddSingleton<HostBroadcasterOAuthStateStore>();
        _ = services.AddDataProtection();
        services.TryAddSingleton<
            IHostBotAccountTokenProtector,
            DataProtectionHostBotAccountTokenProtector
        >();
        _ = services.AddSingleton<HostBotAccountAuthorizationService>();
        _ = services.AddSingleton<HostBroadcasterAuthorizationService>();
        _ = services.AddSingleton<IHostBroadcasterTokenStatusProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBroadcasterAuthorizationService>()
        );
        _ = services.AddSingleton<IBroadcasterAccountProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBroadcasterAuthorizationService>()
        );
        _ = services.AddSingleton<IHostBotAccountTokenStatusProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBotAccountAuthorizationService>()
        );
        switch (appAccessToken)
        {
            case HostBotAppAccessTokenMode.Unavailable:
                _ = services.AddSingleton<
                    IHostBotAppAccessTokenSource,
                    UnavailableHostBotAppAccessTokenSource
                >();
                break;
            case HostBotAppAccessTokenMode.Twitch:
                _ = services.AddSingleton<
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
        _ = services.AddSingleton<HostedChannelChangeNotifier>();
        _ = services.AddSingleton<HostedChannelRuntimeControlService>();
        _ = services.AddSingleton<HostedChannelRuntimeLifecycleService>();
        _ = services.AddSingleton<HostedChannelRuntimeStatusService>();
        _ = services.AddSingleton<HostFeatureService>();
        _ = services.AddSingleton<HostBotStatusService>();
        _ = services.AddSingleton<IHostStreamLivenessProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<HostBotStatusService>()
        );
        _ = services.AddSingleton<ICustomCommandViewerResolver, CustomCommandViewerResolver>();
        _ = services.AddSingleton<FollowerOnlyChatReadinessService>();
        _ = services.AddSingleton<WhisperQuotaService>();
        _ = services.AddSingleton<StartupMessageConfigurationService>();
        _ = services.AddSingleton<IStartupChatMessageProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<StartupMessageConfigurationService>()
        );
        _ = services.AddSingleton<
            IPrivateDeliveryFailureHandler,
            PrivateDeliveryFailureTelemetryHandler
        >();
        _ = services.AddSingleton<WhisperCommandResponseSender>();
        _ = services.AddSingleton<IBotChannelProvider, HostedChannelProvider>();
        _ = services.AddSingleton<HostedChannelLifecycleNotifier>();
        return services;
    }

    public static IServiceCollection AddBlokeBotAuth(this IServiceCollection services)
    {
        _ = services.AddScoped<BlokeBotPageContextAccessor>();
        _ = services.AddSingleton<WebAuthConfiguration>();
        _ = services.AddTransient<ModeratedChannelLookupService>();
        _ = services.AddSingleton<ModeratorAuthorityService>();
        _ = services.AddSingleton<IModeratorAuthorityService>(serviceProvider =>
            serviceProvider.GetRequiredService<ModeratorAuthorityService>()
        );
        _ = services.AddTransient<WebAuthService>();
        _ = services.AddTransient<WebOAuthClient>();
        _ = services.AddScoped<AuthSessionService>();
        _ = services.AddSingleton<IAuthorizationHandler, AuthSessionCapabilityHandler>();
        _ = services.AddTransient<UserLookupService>();
        _ = services.AddTransient<ChannelBotOAuthService>();
        _ = services.AddScoped<AuthCookieValidator>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
