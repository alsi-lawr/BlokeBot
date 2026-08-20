using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Bingo;
using BlokeBot.Core.Features.BlokeRaid;
using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Collectives;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.RaidCollaboration;
using BlokeBot.Core.Features.ViewerPassports;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotCollectives(this IServiceCollection services)
    {
        _ = services.AddSingleton<CollectiveService>();
        _ = services.AddSingleton<ICompetitionLifecycleObserver>(static provider =>
            provider.GetRequiredService<CollectiveService>()
        );
        _ = services.AddSingleton<IBountyChangeObserver>(static provider =>
            provider.GetRequiredService<CollectiveService>()
        );
        _ = services.AddSingleton<IRaidCollaborationDomainEventObserver>(static provider =>
            provider.GetRequiredService<CollectiveService>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotBlokeRaid(this IServiceCollection services)
    {
        _ = services.AddSingleton<IBlokeRaidRandom, BlokeRaidRandom>();
        _ = services.AddSingleton<BlokeRaidService>();
        _ = services.AddSingleton<BlokeRaidRuntime>();
        _ = services.AddSingleton<IBlokeRaidGuessingIntegration>(static serviceProvider =>
            serviceProvider.GetRequiredService<BlokeRaidRuntime>()
        );
        _ = services.AddSingleton<IGuessingChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<BlokeRaidRuntime>()
        );
        _ = services.AddHostedService<BlokeRaidScheduleWorker>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotViewerPassports(this IServiceCollection services)
    {
        _ = services.AddSingleton<ViewerPassportService>();
        _ = services.AddSingleton<ViewerPassportProjectionService>();
        _ = services.AddSingleton<ViewerPassportPublicIdentityPolicy>();
        _ = services.AddSingleton<ViewerPassportRuntime>();
        _ = services.AddSingleton<IChatMessageObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<ViewerPassportRuntime>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotCompetitions(this IServiceCollection services)
    {
        _ = services.AddAutomationCatalogModule<CompetitionAutomationCatalogModule>();
        _ = services.AddSingleton<CompetitionService>();
        _ = services.AddSingleton<
            ICompetitionLifecycleAutomationDispatcher,
            CompetitionLifecycleAutomationDispatcher
        >();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICompetitionLifecycleObserver, CompetitionLifecycleBridge>()
        );
        _ = services.AddSingleton<
            ICompetitionReminderWhisperSender,
            CompetitionReminderWhisperSender
        >();
        _ = services.AddSingleton<ICompetitionReminderDelivery, CompetitionReminderDelivery>();
        _ = services.AddSingleton<CompetitionReminderWorker>();
        _ = services.AddHostedService(static services =>
            services.GetRequiredService<CompetitionReminderWorker>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostFeatureChangeObserver, CompetitionFeatureObserver>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotBingo(this IServiceCollection services)
    {
        _ = services.AddSingleton<BingoService>();
        _ = services.AddSingleton<BingoRuntime>();
        _ = services.AddSingleton<ITwitchEventAutomationObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<BingoRuntime>()
        );
        _ = services.AddSingleton<IEventSubRequirementSource>(static serviceProvider =>
            serviceProvider.GetRequiredService<BingoRuntime>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostFeatureChangeObserver, BingoFeatureObserver>()
        );
        _ = services.AddSingleton<IBountyCompletionObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<BingoRuntime>()
        );
        _ = services.AddSingleton<IGuessingChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<BingoRuntime>()
        );
        _ = services.AddSingleton<IPointsGiveawayChangeObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<BingoRuntime>()
        );
        _ = services.AddSingleton<IBingoCounterEventSink>(static serviceProvider =>
            serviceProvider.GetRequiredService<BingoRuntime>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBingoOverlayEventObserver, BingoOverlayEventPublisher>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotCommunityProgression(
        this IServiceCollection services
    )
    {
        _ = services.AddSingleton<CommunityProgressionService>();
        _ = services.AddSingleton<ICommunityAchievementGrantService>(static serviceProvider =>
            serviceProvider.GetRequiredService<CommunityProgressionService>()
        );
        _ = services.AddSingleton<CommunityProgressionRuntime>();
        _ = services.AddSingleton<ITwitchEventAutomationObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<CommunityProgressionRuntime>()
        );
        _ = services.AddSingleton<IChatMessageObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<CommunityProgressionRuntime>()
        );
        _ = services.AddSingleton<IEventSubRequirementSource>(static serviceProvider =>
            serviceProvider.GetRequiredService<CommunityProgressionRuntime>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostFeatureChangeObserver,
                CommunityProgressionFeatureObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IBountyCompletionObserver,
                BountyCommunityProgressionObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CommunityProgressionScheduleWorker>()
        );
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddBlokeBotBounties(this IServiceCollection services)
    {
        _ = services.AddSingleton<BountyService>();
        _ = services.AddSingleton<BountyPauseObserver>();
        _ = services.AddSingleton<IHostFeatureChangeObserver>(static services =>
            services.GetRequiredService<BountyPauseObserver>()
        );
        _ =
            services.AddSingleton<BlokeBot.Core.Features.ConfigurationTransfer.IConfigurationActivationObserver>(
                static services => services.GetRequiredService<BountyPauseObserver>()
            );
        _ = services.AddSingleton(
            new BountyExpirySchedulerPolicy
            {
                PollInterval = TimeSpan.FromSeconds(30),
                BatchSize = 100,
            }
        );
        _ = services.AddSingleton<BountyExpiryScheduler>();
        _ = services.AddHostedService(static sp => sp.GetRequiredService<BountyExpiryScheduler>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
