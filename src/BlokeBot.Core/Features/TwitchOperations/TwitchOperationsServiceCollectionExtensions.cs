using BlokeBot.Core.Features.TwitchOperations.Shoutouts;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.TwitchOperations;

public static class TwitchOperationsServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotTwitchOperations(this IServiceCollection services)
    {
        services.TryAddSingleton<ShoutoutService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IShoutoutEventObserver>(provider =>
                provider.GetRequiredService<ShoutoutService>()
            )
        );
        return services;
    }
}
