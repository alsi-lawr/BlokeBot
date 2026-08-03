using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.TwitchOperations;

public static class TwitchOperationsServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotTwitchOperations(this IServiceCollection services)
    {
        _ = services.AddSingleton<NativeTwitchFeatureGate>();
        _ = services.AddSingleton<INativeTwitchFeatureStateProvider>(provider =>
            provider.GetRequiredService<NativeTwitchFeatureGate>()
        );
        _ = services.AddSingleton<ShoutoutService>();
        _ = services.AddSingleton<IShoutoutDashboardOperations>(provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        _ = services.AddSingleton<IAutomaticRaidNativeShoutoutOperation>(provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        _ = services.AddSingleton<AutomaticRaidShoutoutConfigurationService>();
        services.TryAddSingleton<
            IAutomaticRaidNativeShoutoutSender,
            AutomaticRaidNativeShoutoutSender
        >();
        services.TryAddSingleton<
            IAutomaticRaidChannelInformationProvider,
            AutomaticRaidChannelInformationProvider
        >();
        services.TryAddSingleton<
            IAutomaticRaidAnnouncementSender,
            AutomaticRaidAnnouncementSender
        >();
        services.TryAddSingleton<IAutomaticRaidShoutoutDelivery, AutomaticRaidShoutoutDelivery>();
        _ = services.AddSingleton<AutomaticRaidShoutoutObserver>();
        _ = services.AddSingleton<PollService>();
        _ = services.AddSingleton<IPollDashboardOperations>(provider =>
            provider.GetRequiredService<PollService>()
        );
        _ = services.AddSingleton<ClipMarkerService>();
        _ = services.AddSingleton<IClipMarkerDashboardOperations>(provider =>
            provider.GetRequiredService<ClipMarkerService>()
        );
        _ = services.AddSingleton<ChannelPointsService>();
        _ = services.AddSingleton<IChannelPointsDashboardOperations>(provider =>
            provider.GetRequiredService<ChannelPointsService>()
        );
        _ = services.AddSingleton(provider => new PredictionService(
            provider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
            provider.GetRequiredService<IHostBroadcasterTokenStatusProvider>(),
            provider.GetRequiredService<HelixClient>(),
            provider.GetRequiredService<BotSettings>(),
            provider.GetRequiredService<EventBus<AppEventKind>>(),
            provider.GetRequiredService<DurableAlertService>(),
            provider.GetRequiredService<ILogger<PredictionService>>(),
            provider.GetRequiredService<NativeTwitchFeatureGate>(),
            TimeProvider.System
        ));
        _ = services.AddSingleton<IPredictionDashboardOperations>(provider =>
            provider.GetRequiredService<PredictionService>()
        );
        _ = services.AddSingleton<
            INativeTwitchFeatureChangeObserver,
            NativeTwitchFeatureChangeObserver
        >();
        _ = services.AddSingleton<IPollEventObserver>(provider =>
            provider.GetRequiredService<PollService>()
        );
        _ = services.AddSingleton<IPredictionEventObserver>(provider =>
            provider.GetRequiredService<PredictionService>()
        );
        _ = services.AddSingleton<IChannelPointsEventObserver>(provider =>
            provider.GetRequiredService<ChannelPointsService>()
        );
        _ = services.AddSingleton<IShoutoutEventObserver>(provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        _ = services.AddSingleton<IIncomingRaidEventObserver>(provider =>
            provider.GetRequiredService<AutomaticRaidShoutoutObserver>()
        );
        return services;
    }
}
