using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace BlokeBot.Core.Features.TwitchOperations;

public static class TwitchOperationsServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotTwitchOperations(this IServiceCollection services)
    {
        services.AddSingleton<ShoutoutService>();
        services.AddSingleton<IShoutoutEventObserver>(provider =>
            provider.GetRequiredService<ShoutoutService>()
        );
        return services;
    }
}
