using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<PollService>();
        services.AddSingleton<ClipMarkerService>();
        services.AddSingleton<ChannelPointsService>();
        services.AddSingleton<PredictionService>();
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
        return services;
    }
}
