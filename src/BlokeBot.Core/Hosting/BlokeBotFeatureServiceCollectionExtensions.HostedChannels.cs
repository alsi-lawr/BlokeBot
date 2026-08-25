using BlokeBot.Core.BotRuntime;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.HostConfig.Access;
using BlokeBot.Core.Features.HostConfig.Page;
using BlokeBot.Core.Features.HostConfig.StartupMessage;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.HostedChannels.Whispers;
using BlokeBot.Core.Features.PublicLeaderboards;
using BlokeBot.Core.Hosts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
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
        _ = services.AddSingleton<IHostBroadcasterTokenStatusProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<HostBroadcasterAuthorizationService>()
        );
        _ = services.AddSingleton<IBroadcasterAccountProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<HostBroadcasterAuthorizationService>()
        );
        _ = services.AddSingleton<IHostBotAccountTokenStatusProvider>(static serviceProvider =>
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
        _ = services.AddSingleton<HostedChannelRuntimeTransitionService>();
        _ = services.AddSingleton<HostedChannelRuntimeControlService>();
        _ = services.AddSingleton<HostedChannelRuntimeLifecycleService>();
        _ = services.AddSingleton<HostedChannelRuntimeStatusService>();
        _ = services.AddSingleton<HostFeatureService>();
        _ = services.AddSingleton<HostBotStatusService>();
        _ = services.AddSingleton<IHostStreamLivenessProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<HostBotStatusService>()
        );
        _ = services.AddSingleton<ICustomCommandViewerResolver, CustomCommandViewerResolver>();
        _ = services.AddSingleton<FollowerOnlyChatReadinessService>();
        _ = services.AddSingleton<WhisperQuotaService>();
        _ = services.AddSingleton<StartupMessageConfigurationService>();
        _ = services.AddSingleton<IStartupChatMessageProvider>(static serviceProvider =>
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
}
