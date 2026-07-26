using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Adds Twitch authentication services to an application service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the low-level Twitch OAuth transport.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddOAuthTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient("twitch-oauth");
        services.TryAddSingleton(TwitchEndpointPolicy.Default);
        services.TryAddSingleton<OAuthTransport>();

        return services;
    }

    /// <summary>
    /// Registers OAuth flows and token providers for a configured Twitch bot identity.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAuth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOAuthTransport();
        services.TryAddSingleton<AppAccessTokenProvider>();
        services.TryAddSingleton<ITokenStore, JsonTokenStore>();
        services.TryAddSingleton<IOAuthStateStore, InMemoryOAuthStateStore>();
        services.TryAddSingleton<IOAuthClient, OAuthClient>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOAuthFlow, OAuthFlow>();
        services.TryAddSingleton<IAccessTokenCache, AccessTokenCache>();
        services.TryAddSingleton<IAccessTokenProvider, AccessTokenProvider>();

        return services;
    }

    /// <summary>
    /// Registers the unavailable access-token capability for an unconfigured bot runtime.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddUnavailableAccessTokenProvider(
        this IServiceCollection services
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAccessTokenProvider, UnavailableAccessTokenProvider>();

        return services;
    }
}
