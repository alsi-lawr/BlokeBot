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
using BlokeBot.Twitch;
using BlokeBot.Twitch.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Core.Features.TwitchOperations;

public static class TwitchOperationsServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotTwitchOperations(this IServiceCollection services)
    {
        services.AddSingleton<NativeTwitchFeatureGate>();
        services.AddSingleton<INativeTwitchFeatureStateProvider>(provider =>
            provider.GetRequiredService<NativeTwitchFeatureGate>()
        );
        services.AddSingleton<ShoutoutService>();
        services.AddSingleton<AutomaticRaidShoutoutConfigurationService>();
        services.TryAddSingleton<
            IAutomaticRaidShoutoutDelivery,
            UnavailableAutomaticRaidShoutoutDelivery
        >();
        services.AddSingleton<AutomaticRaidShoutoutObserver>();
        services.AddSingleton<PollService>();
        services.AddSingleton<ClipMarkerService>();
        services.AddSingleton<ChannelPointsService>();
        services.AddSingleton(provider => new PredictionService(
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
        services.AddSingleton<
            INativeTwitchFeatureChangeObserver,
            NativeTwitchFeatureChangeObserver
        >();
        services.AddSingleton<IPollEventObserver>(provider =>
            provider.GetRequiredService<PollService>()
        );
        services.AddSingleton<IPredictionEventObserver>(provider =>
            provider.GetRequiredService<PredictionService>()
        );
        services.AddSingleton<IChannelPointsEventObserver>(provider =>
            provider.GetRequiredService<ChannelPointsService>()
        );
        services.AddSingleton<IShoutoutEventObserver>(provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        services.AddSingleton<IIncomingRaidEventObserver>(provider =>
            provider.GetRequiredService<AutomaticRaidShoutoutObserver>()
        );
        return services;
    }
}
