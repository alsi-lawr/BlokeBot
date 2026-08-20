using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.MomentAttachments;
using BlokeBot.Core.Features.Moments;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Core.Features.PlayWithViewers;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Core.Hosts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
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
        _ = services.AddSingleton<IPlayQueueProjectionReader>(static serviceProvider =>
            serviceProvider.GetRequiredService<PlayQueueService>()
        );
        _ = services.AddSingleton<IPrivateLobbyDelivery, TwitchPrivateLobbyDelivery>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotMoments(this IServiceCollection services)
    {
        _ = services.AddSingleton<MomentHubService>();
        _ = services.AddSingleton<MomentAttachmentService>();
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
        _ = services.AddSingleton<IOverlayEventPresenter>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayEventFeedService>()
        );
        _ = services.AddSingleton<IHostFeatureChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayEventFeedService>()
        );
        _ = services.AddHostedService(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayEventFeedService>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ICommunityAchievementCompletionObserver,
                CommunityAchievementOverlayEventPublisher
            >()
        );
        _ = services.AddSingleton<IOverlayStateProvider, OverlayStateProvider>();
        _ = services.AddSingleton<OverlayLiveCoordinator>();
        _ = services.AddSingleton<IOverlayLivePublisher>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IOverlayEventFeedLivePublisher>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IOverlayLivePresence>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IOverlayCueTransport>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<OverlayCuePlaybackService>();
        _ = services.AddSingleton<IOverlayCueAdmissionService>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayCuePlaybackService>()
        );
        _ = services.AddSingleton<IGuessingChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IPointsGiveawayChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<IBountyChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddSingleton<ICommunityProgressionChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddHostedService(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayLiveCoordinator>()
        );
        _ = services.AddHostedService(static serviceProvider =>
            serviceProvider.GetRequiredService<OverlayCuePlaybackService>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
