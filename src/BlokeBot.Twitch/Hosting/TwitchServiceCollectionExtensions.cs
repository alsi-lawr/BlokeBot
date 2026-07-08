using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch;

/// <summary>
/// Adds Twitch platform services to an application service collection.
/// </summary>
public static class TwitchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Twitch Helix API client.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddTwitchHelix(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient("twitch-helix");
        services.TryAddSingleton<TwitchHelixApiClient>();

        return services;
    }
}
