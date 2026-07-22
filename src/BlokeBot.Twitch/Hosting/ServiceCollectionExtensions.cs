using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch;

/// <summary>
/// Adds Twitch platform services to an application service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twitch Helix transport clients.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHelix(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient("twitch-helix");
        services.TryAddSingleton<HelixClient>();
        services.TryAddSingleton<EventSubClient>();
        services.TryAddSingleton<ChatClient>();
        services.TryAddSingleton<ChatAnnouncementClient>();
        services.TryAddSingleton<ChatPinClient>();
        services.TryAddSingleton<WhisperClient>();

        return services;
    }
}
